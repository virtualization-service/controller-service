using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Steeltoe.CloudFoundry.Connector.RabbitMQ;
using ControllerService.Processors;
using Microsoft.AspNetCore.Http;
using System.IO;
using Parser.Model;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Parser
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options=> 
            {
                options.AddPolicy("AllowOrigin", 
                builder=> builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

            });
            services.AddScoped<PublishMessage>();
            services.AddSingleton<MessageConsumer>();
            services.AddControllers().AddNewtonsoftJson();

            services.AddRabbitMQConnection(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ConnectionFactory factory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseCors();

            var processors = app.ApplicationServices.GetService<MessageConsumer>();
            var life =  app.ApplicationServices.GetService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
            life.ApplicationStarted.Register(GetOnStarted(factory, processors));
            life.ApplicationStopping.Register(GetOnStopped(factory, processors));

            app.Run(async context => 
            {
                try
                {
                    var requestPath = context.Request.Path.Value;
                    Console.WriteLine($"Request Path {requestPath} and with method {context.Request.Method}");

                    if(string.IsNullOrEmpty(requestPath)
                    || requestPath == "/" || requestPath =="/favicon.ico" || requestPath =="/cloudfoundryapplication" )
                    {
                        await context.Response.WriteAsync("Running");
                    }

                    if(requestPath.ToLower().Equals("/virtualization-train"))
                    {
                        var publisher = app.ApplicationServices.GetService<PublishMessage>();

                        var messageToPublish = ConvertToString(context.Request?.Body);

                        if(string.IsNullOrEmpty(messageToPublish)){
                            await context.Response.WriteAsync("Provide data in request body to learn");
                        }
                        else
                        {
                            publisher.Publish(messageToPublish , factory);
                            await context.Response.WriteAsync("Learning is in progress, should be completed by the time you can read this.");
                        }

                        return;
                    }

                    var headers = new Dictionary<string, string>();

                    foreach(var header in context.Request.Headers)
                    {
                        if(header.Key == "Content-Length") continue;

                        headers.Add(header.Key, header.Value.FirstOrDefault());
                    }

                    var messageBody = ConvertToString(context.Request?.Body);

                    var message = new MessageDto
                    {
                        service = new System.Uri(context.Request.Scheme+"://" + context.Request.Host + context.Request.Path),
                        request =  new Body{
                            raw_data = messageBody,
                            headers = headers
                        }
                    };

                    var serializedMessage = JsonConvert.SerializeObject(message);

                    using(var virtualizer =  new Virtualizer(factory))
                    {
                        var response = await virtualizer.CallASync(serializedMessage, factory);

                        if(response == null) await context.Response.WriteAsync("Error Generating Response");

                        var jo =  JObject.Parse(response);

                        context.Response.Headers.TryAdd("confidence", Convert.ToString(jo.SelectToken("data.confidence")));
                        context.Response.Headers.TryAdd("rank", Convert.ToString(jo.SelectToken("data.rank")));
                        context.Response.Headers.TryAdd("propertiesMatched", Convert.ToString(jo.SelectToken("data.confidence")));
                        context.Response.Headers.TryAdd("Content-Type", "application/json;charset=UTF-8");

                        await context.Response.WriteAsync(Convert.ToString(jo.SelectToken("data.response.raw_data")) ?? "No Data Found");
                    }
                }
                catch
                {

                }

            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        private static Action GetOnStarted(ConnectionFactory factory, MessageConsumer processors)
        {
            return () => {processors.Register(factory);};
        }

        private static Action GetOnStopped(ConnectionFactory factory, MessageConsumer processors)
        {
            return () => {processors.DeRegister(factory);};
        }

        private static string ConvertToString(Stream stream)
        {
            if(stream == null) return string.Empty;

            var rdr =  new StreamReader(stream);

            return rdr.ReadToEnd();
        }
    }
}
