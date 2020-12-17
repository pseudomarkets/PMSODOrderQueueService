using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using JWT.Algorithms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PMCommonEntities.Models;
using Serilog;
using JWT.Builder;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PMCommonApiModels.RequestModels;

namespace PMSODOrderQueueService
{
    public class Program
    {
        public static IConfigurationRoot Configuration;
        public static string BaseUrl = string.Empty;
        public static string TokenSource = string.Empty;
        public static string TokenSecret = string.Empty;
        public static string TokenIssuer = string.Empty;

        static void Main(string[] args)
        {
            // Setup the Serilog logger
            string logFileName = "PMSODOrderQueueService-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logFileName)
                .CreateLogger();
            Log.Information("Starting PMSODOrderQueueService");

            int ordersExecuted = PerformQueuedOrderExecution();

            Log.Information($"Orders executed on {DateTime.Now}: {ordersExecuted}");
        }

        private static void ConfigureServices(IServiceCollection serviceCollection)
        {
            // Inject Serilog
            serviceCollection.AddSingleton(LoggerFactory.Create(builder =>
            {
                builder.AddSerilog(dispose: true);
            }));

            serviceCollection.AddLogging();

            // Setup our application config
            Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", false)
                .Build();

            serviceCollection.AddSingleton<IConfigurationRoot>(Configuration);

            serviceCollection.AddTransient<Program>();

            BaseUrl = Configuration.GetValue<string>("PMConfig:BaseUrl");
            TokenSource = Configuration.GetValue<string>("PMConfig:TokenSource");
            TokenSecret = Configuration.GetValue<string>("PMConfig:TokenSecretKey");
            TokenIssuer = Configuration.GetValue<string>("PMConfig:TokenIssuer");
        }

        private static int PerformQueuedOrderExecution()
        {
            int numRecordsAffected = 0;
            ServiceCollection serviceCollection = new ServiceCollection();

            // Configure the config service so we can fetch the connection string from it
            ConfigureServices(serviceCollection);

            try
            {
                using (var db = new PseudoMarketsDbContext())
                {
                    List<QueuedOrders> queuedOrders = new List<QueuedOrders>();

                    // Step 0: Perform filtering based on the day of the week. Executions on Monday morning need to include orders placed after hours on Friday through Sunday night. 
                    if (DateTime.Now.DayOfWeek == DayOfWeek.Monday)
                    {
                        queuedOrders = db.QueuedOrders.Where(x =>
                                x.IsOpenOrder && x.OrderDate >= DateTime.Today.AddDays(-3) && x.OrderDate <= DateTime.Today)
                            .ToList();
                    }
                    else
                    {
                        queuedOrders = db.QueuedOrders.Where(x => x.IsOpenOrder && x.OrderDate == DateTime.Today.AddDays(-1))
                            .ToList();
                    }

                    Log.Information($"Found {queuedOrders.Count} queued orders on {DateTime.Now}");

                    foreach (QueuedOrders order in queuedOrders)
                    {
                        // Step 1: Create a new special auth token to place a proxied order
                        string tempToken = GenerateToken($"SODOrderProxy-{order.UserId}", TokenType.Special);

                        var token = db.Tokens.FirstOrDefault(x => x.UserID == order.UserId);
                        if (token != null)
                        {
                            token.Token = tempToken;
                            db.Entry(token).State = EntityState.Modified;
                            db.SaveChanges();
                        }

                        // Step 2: Create an order object and POST it to the Trading API
                        TradeExecInput orderInput = new TradeExecInput()
                        {
                            Token = tempToken,
                            Symbol = order.Symbol,
                            Quantity = order.Quantity,
                            Type = order.OrderType
                        };

                        var client = new HttpClient
                        {
                            BaseAddress = new Uri(BaseUrl)
                        };

                        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Trade/Execute");
                        string jsonRequest = JsonConvert.SerializeObject(orderInput);

                        var stringContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                        request.Content = stringContent;

                        var response = client.SendAsync(request);
                        var responseString = response.Result.Content.ReadAsStringAsync();

                        // Step 3: Log the result
                        Log.Information(responseString.Result);

                        // Step 4: Remove the queued order
                        db.Entry(order).State = EntityState.Deleted;
                        
                        numRecordsAffected++;

                    }

                    db.SaveChanges();

                }
            }
            catch (Exception e)
            {
                Log.Error(e, $"{nameof(PerformQueuedOrderExecution)}");
                return -1;
            }

            return numRecordsAffected;
        }

        public static string GenerateToken(string username, TokenType type)
        {
            var token = new JwtBuilder()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .WithSecret(TokenSecret)
                .AddClaim("sub", username)
                .AddClaim("typ", type)
                .AddClaim("iss", TokenIssuer)
                .AddClaim("src", TokenSource)
                .AddClaim("ts", DateTime.Now)
                .Encode();
            return token;
        }
    }

    public enum TokenType
    {
        Standard = 1,
        Special = 2
    }
}
