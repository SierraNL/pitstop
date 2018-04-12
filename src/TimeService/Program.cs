using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Runtime;
using Pitstop.Infrastructure.Messaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using tim = TimeService;

namespace Pitstop.TimeService
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
            try
            {
                ServiceRuntime.RegisterServiceAsync("TimeServiceType",
                    context => new tim.TimeService(context)).GetAwaiter().GetResult();

                tim.ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(tim.TimeService).Name);

                // get configuration
                var configSection = Config.GetSection("RabbitMQ");
                string host = configSection["Host"];
                string userName = configSection["UserName"];
                string password = configSection["Password"];

                // start time manager
                RabbitMQMessagePublisher messagePublisher = new RabbitMQMessagePublisher(host, userName, password, "Pitstop");
                TimeManager manager = new TimeManager(messagePublisher);
                manager.Start();

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                tim.ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}