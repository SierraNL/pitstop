using inv = InvoiceService;
using Microsoft.Extensions.Configuration;
using Microsoft.ServiceFabric.Services.Runtime;
using Pitstop.Infrastructure.Messaging;
using Pitstop.InvoiceService.CommunicationChannels;
using Pitstop.InvoiceService.Repositories;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Pitstop.InvoiceService
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

        static void Main(string[] args)
        {
            try
            {
                ServiceRuntime.RegisterServiceAsync("InvoiceServiceType",
                        context => new inv.InvoiceService(context)).GetAwaiter().GetResult();

                inv.ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(inv.InvoiceService).Name);

                // get configuration
                var configSection = Config.GetSection("RabbitMQ");
                string host = configSection["Host"];
                string userName = configSection["UserName"];
                string password = configSection["Password"];

                var sqlConnectionString = Config.GetConnectionString("InvoiceServiceCN");

                var mailConfigSection = Config.GetSection("Email");
                string mailHost = mailConfigSection["Host"];
                int mailPort = Convert.ToInt32(mailConfigSection["Port"]);
                string mailUserName = mailConfigSection["User"];
                string mailPassword = mailConfigSection["Pwd"];

                // start invoice manager
                var messageHandler = new RabbitMQMessageHandler(host, userName, password, "Pitstop", "Invoicing", "");
                IInvoiceRepository repo = new SqlServerInvoiceRepository(sqlConnectionString);
                IEmailCommunicator emailCommunicator = new SMTPEmailCommunicator(mailHost, mailPort, mailUserName, mailPassword);
                var manager = new InvoiceManager(messageHandler, repo, emailCommunicator);
                manager.Start();

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                inv.ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}