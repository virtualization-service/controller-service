using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

namespace ControllerService.Processors
{
    public class Virtualizer : IDisposable
    {

        private IConnection connection;

        private IModel channel;

        private readonly string replyQueueName;

        private readonly EventingBasicConsumer consumer;

        private ConcurrentDictionary<string, TaskCompletionSource<string>> callBackMapper = new ConcurrentDictionary<string, TaskCompletionSource<string>>();


        public Virtualizer(ConnectionFactory factory)
        {
            connection = factory.CreateConnection();
            channel = connection.CreateModel();
            channel.ConfirmSelect();
            replyQueueName = "vir_response";
            consumer = new EventingBasicConsumer(channel);
            
            consumer.Received += (consumerModel ,ea) =>
            {
                if(!this.callBackMapper.TryRemove(ea.BasicProperties.CorrelationId ?? string.Empty, out TaskCompletionSource<string> tcs))
                {
                    return;
                }
                var body = ea.Body;
                var response = Encoding.UTF8.GetString(body);

                this.channel.BasicAck(ea.DeliveryTag, false);
                tcs.TrySetResult(response);
            };
            channel.BasicQos(0,10000,false);
            channel.BasicConsume(replyQueueName,autoAck:false, consumer: consumer);
        }

        public Task<string> CallASync(string message, ConnectionFactory factory, CancellationToken cancellationToken = default(CancellationToken))
        {
            IBasicProperties props = channel.CreateBasicProperties();
            var correlationId = Guid.NewGuid().ToString();
            props.CorrelationId = correlationId;
            props.ReplyTo = "vir_response";
            var messageBytes = Encoding.UTF8.GetBytes(message);

            var tcs = new TaskCompletionSource<string>();
            const int timeoutMs = 20000;

            var ct = new CancellationTokenSource(timeoutMs);
            ct.Token.Register(()=> tcs.TrySetCanceled(), false);

            this.callBackMapper.TryAdd(correlationId, tcs);

            channel.BasicPublish(exchange:"virtualization",
                routingKey : "virtualization.begin",
                basicProperties: props,
                body: messageBytes);

            channel.WaitForConfirmsOrDie(new TimeSpan(0,0,5));

            cancellationToken.Register(()=> callBackMapper.TryRemove(correlationId, out var tmp));

            return tcs.Task;
        }

        public void Dispose()
        {
            if(channel != null && !channel.IsClosed)
            {
                channel.Close();
            }
        }
    
    }
}