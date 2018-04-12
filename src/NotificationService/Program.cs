using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Runtime;
using not = NotificationService;
using Pitstop.Infrastructure.Messaging;
using Pitstop.NotificationService.NotificationChannels;
using Pitstop.NotificationService.Repositories;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Pitstop.NotificationService
{
    class Program
    {
        private static string _env;
        public static IConfigurationRoot Config { get; private set; }

        static Program()
        {
            _env = Environment.GetEnvironmentVariable("SERVICE_ENVIRONMENT");

            Console.WriteLine($"Environment: {_env}");

            Config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{_env}.json", optional: false)
                .Build();
        }

        static void Main(string[] args)
        {
            try {
                ServiceRuntime.RegisterServiceAsync("NotificationServiceType",
                    context => new not.NotificationService(context)).GetAwaiter().GetResult();

                not.ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(not.NotificationService).Name);

                // get configuration
                var rmqConfigSection = Config.GetSection("RabbitMQ");
                string rmqHost = rmqConfigSection["Host"];
                string rmqUserName = rmqConfigSection["UserName"];
                string rmqPassword = rmqConfigSection["Password"];

                var mailConfigSection = Config.GetSection("Email");
                string mailHost = mailConfigSection["Host"];
                int mailPort = Convert.ToInt32(mailConfigSection["Port"]);
                string mailUserName = mailConfigSection["User"];
                string mailPassword = mailConfigSection["Pwd"];

                var sqlConnectionString = Config.GetConnectionString("NotificationServiceCN");

                // start notification service
                RabbitMQMessageHandler messageHandler = 
                    new RabbitMQMessageHandler(rmqHost, rmqUserName, rmqPassword, "Pitstop", "Notifications", "");
                INotificationRepository repo = new SqlServerNotificationRepository(sqlConnectionString);
                IEmailNotifier emailNotifier = new SMTPEmailNotifier(mailHost, mailPort, mailUserName, mailPassword);
                NotificationManager manager = new NotificationManager(messageHandler, repo, emailNotifier);
                manager.Start();

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                not.ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}