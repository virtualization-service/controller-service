using System;
using System.Text;
using RabbitMQ.Client;

namespace ControllerService.Processors
{
    public class PublishMessage
    {
        public void Publish(string message, ConnectionFactory _factory, string exchange= "configuration", IBasicProperties properties = null)
        {
            PublishSingleMessage(message, _factory, exchange, properties);
        }

        private void PublishSingleMessage(string message, ConnectionFactory _factory, string exchange, IBasicProperties properties)
        {
            if(!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("RABBIT_MQ_URI")))
            {
                _factory.Uri = new Uri(System.Environment.GetEnvironmentVariable("RABBIT_MQ_URI"));
            }

            using (var connection = _factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var props = properties ?? channel.CreateBasicProperties();
                var queueName = channel.QueueDeclare().QueueName;
                channel.ConfirmSelect();
                var body = Encoding.UTF8.GetBytes(message);
                channel.BasicPublish(exchange: exchange, routingKey: "configuration.train", basicProperties: props, body: body);
                channel.WaitForConfirmsOrDie(new TimeSpan(0,0,5));
            }
        }

    }
}