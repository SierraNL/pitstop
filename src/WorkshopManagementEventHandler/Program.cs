using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Runtime;
using Pitstop.Infrastructure.Messaging;
using Pitstop.WorkshopManagementEventHandler.DataAccess;
using Polly;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Pitstop.WorkshopManagementEventHandler
{
    class Program
    {
        private static string _env;
        public static IConfigurationRoot Config { get; private set; }

        static Program()
        {
            _env = Environment.GetEnvironmentVariable("PITSTOP_ENVIRONMENT") ?? "Production";

            Console.WriteLine($"Environment: {_env}");

            Config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{_env}.json", optional: false)
                .Build();
        }

        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            try
            {
                // The ServiceManifest.XML file defines one or more service type names.
                // Registering a service maps a service type name to a .NET type.
                // When Service Fabric creates an instance of this service type,
                // an instance of the class is created in this host process.

                ServiceRuntime.RegisterServiceAsync("WorkshopManagementEventHandlerType",
                    context => new WorkshopManagementEventHandler(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(WorkshopManagementEventHandler).Name);

                // get configuration
                var rabbitMQConfigSection = Config.GetSection("RabbitMQ");
                string host = rabbitMQConfigSection["Host"];
                string userName = rabbitMQConfigSection["UserName"];
                string password = rabbitMQConfigSection["Password"];

                // setup messagehandler
                RabbitMQMessageHandler messageHandler = new RabbitMQMessageHandler(host, userName, password, "Pitstop", "WorkshopManagement", "");

                // setup DBContext
                var sqlConnectionString = Config.GetConnectionString("WorkshopManagementCN");
                var dbContextOptions = new DbContextOptionsBuilder<WorkshopManagementDBContext>()
                    .UseSqlServer(sqlConnectionString)
                    .Options;
                var dbContext = new WorkshopManagementDBContext(dbContextOptions);

                Policy
                .Handle<Exception>()
                .WaitAndRetry(5, r => TimeSpan.FromSeconds(5), (ex, ts) => { Console.WriteLine("Error connecting to DB. Retrying in 5 sec."); })
                .Execute(() => DBInitializer.Initialize(dbContext));

                // start event-handler
                EventHandler eventHandler = new EventHandler(messageHandler, dbContext);
                eventHandler.Start();

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}