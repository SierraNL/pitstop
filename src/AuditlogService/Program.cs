﻿using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Runtime;
using Pitstop.Infrastructure.Messaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AuditlogService
{
    class Program
    {
        private static string _env;
        public static IConfigurationRoot Config { get; private set; }

        static Program()
        {
            _env = Environment.GetEnvironmentVariable("SERVICE_ENVIRONMENT") ?? "Production";

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

                ServiceRuntime.RegisterServiceAsync("AuditlogServiceType",
                    context => new AuditlogService(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(AuditlogService).Name);

                // get configuration
                var rabbitMQConfigSection = Config.GetSection("RabbitMQ");
                string host = rabbitMQConfigSection["Host"];
                string userName = rabbitMQConfigSection["UserName"];
                string password = rabbitMQConfigSection["Password"];

                var auditlogConfigSection = Config.GetSection("Auditlog");
                string logPath = auditlogConfigSection["path"];

                // start auditlog manager
                RabbitMQMessageHandler messageHandler = new RabbitMQMessageHandler(host, userName, password, "Pitstop", "Auditlog", "");
                AuditLogManager manager = new AuditLogManager(messageHandler, logPath);
                manager.Start();

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