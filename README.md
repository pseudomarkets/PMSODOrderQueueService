# PMSODOrderQueueService
A .NET Core application designed to be run using Task Scheduler or cron that executes queued orders from a previous market day

# Requirements
* .NET Core 3.1
* Pseudo Markets instance with the latest database updates

# Usage
The PMSODOrderQueueService is meant to be run as an SOD proccess that can be scheduled through Task Scheduler on Windows or cron on Linux. As a start of day service, it should be run at market open (9:30 AM EST) during regular market open days. 
