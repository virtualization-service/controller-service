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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Xml.Linq;

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
            
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
            services.AddSingleton<MessageConsumer>();
            services.AddTransient<PublishMessage>();

            services.AddRabbitMQConnection(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IHostingEnvironment env, ConnectionFactory factory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseCors();

            var processors = app.ApplicationServices.GetService<MessageConsumer>();
            var life =  app.ApplicationServices.GetService<Microsoft.Extensions.Hosting.IApplicationLifetime>();
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

                    if( requestPath == "/config")
                    {
                        var environments = Environment.GetEnvironmentVariables();
                        XElement data1 = new XElement("parent");
                        var data = "";

                        foreach(var val in environments.Keys)
                        {
                            //data += val.ToString() +":" + Environment.GetEnvironmentVariable(val.ToString()) + "||";
                            data1.Add(new XElement(val.ToString(), Environment.GetEnvironmentVariable(val.ToString())));
                            await context.Response.WriteAsync(data1.ToString());
                            return;
                        }

                        await context.Response.WriteAsync("Running");
                    }

                    if(requestPath.ToLower().Equals("/virtualization-train"))
                    {
                        var publisher = app.ApplicationServices.GetService<PublishMessage>();

                        var messageToPublish = await ConvertToString(context.Request?.Body);

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

                    var messageBody = await ConvertToString(context.Request?.Body);

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
                catch(Exception ex)
                {

                }

            });

           app.UseMvc();
        }

        private static Action GetOnStarted(ConnectionFactory factory, MessageConsumer processors)
        {
            return () => {processors.Register(factory);};
        }

        private static Action GetOnStopped(ConnectionFactory factory, MessageConsumer processors)
        {
            return () => {processors.DeRegister(factory);};
        }

        private static Task<string> ConvertToString(Stream stream)
        {
            var rdr =  new StreamReader(stream);

            return rdr.ReadToEndAsync();
        }
    }
}
