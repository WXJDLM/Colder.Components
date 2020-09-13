﻿using Colder.Logging.Serilog;
using Colder.MessageBus.Abstractions;
using Colder.MessageBus.MassTransit;
using Demo.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace Demo.MessageBus.Producer
{
    class Program
    {
        public static async Task Main()
        {
            await Host.CreateDefaultBuilder()
               .ConfigureLoggingDefaults()
               .ConfigureServices(services =>
               {
                   services.AddHostedService<SendTest>();
                   services.AddMessageBus(new MessageBusOptions
                   {
                       Host = "localhost",
                       Transport = TransportType.RabbitMQ
                   }, MessageBusEndpoints.Producer);
               })
               .RunConsoleAsync();
        }
    }
}
