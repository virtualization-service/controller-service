using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;

namespace ControllerService.Processors
{
    public class MessageConsumer
    {

        // Virtualizer _extractor;

        private IModel _channel;

        public MessageConsumer()
        {
            //_extractor = extractor;
        }

        public void Register(ConnectionFactory factory)
        {
            if(!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("RABBIT_MQ_URI")))
            {
                factory.Uri = new Uri(System.Environment.GetEnvironmentVariable("RABBIT_MQ_URI"));
            }

            var connection = factory.CreateConnection();
           
            _channel = connection.CreateModel();

            _channel.ExchangeDeclare("configuration", type: "topic", durable: true);
            _channel.ExchangeDeclare("virtualization", type: "topic", durable: true);

            _channel.QueueDeclare("vir_response",false,false,false,new Dictionary<string,object>{{"x-message-ttl", 60000}});
            _channel.QueueBind("vir_response","virtualization", "evaluator.completed");
            

            //_channel.ConfirmSelect();

            //_channel.BasicQos(0,10000, false);
        }

        public void DeRegister(ConnectionFactory factory)
        {
            if(_channel != null) _channel.Close();
        }
    }
}