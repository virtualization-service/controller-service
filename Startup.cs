using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Steeltoe.CloudFoundry.Connector.RabbitMQ;
using ControllerService.Processors;

namespace Parser
{
    public class Startup
    {
        readonly string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options =>
           {
               options.AddPolicy(name: MyAllowSpecificOrigins,
                               builder =>
                               {
                                   builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                               });
           });

            services.AddControllers();
            services.AddSingleton<PublishMessage>();
            services.AddSingleton<MessageConsumer>();

            services.AddRabbitMQConnection(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ConnectionFactory factory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var processors = app.ApplicationServices.GetService<MessageConsumer>();
            var life = app.ApplicationServices.GetService<IHostApplicationLifetime>();
            life.ApplicationStarted.Register(GetOnStarted(factory, processors));
            life.ApplicationStopping.Register(GetOnStopped(factory, processors));
            app.UseRouting();

            app.Run(async context => await new RequestProcessor().ProcessResponseAsync(context, app, factory));
            app.UseCors(MyAllowSpecificOrigins);

            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        private static Action GetOnStarted(ConnectionFactory factory, MessageConsumer processors)
        {
            return () => { processors.Register(factory); };
        }

        private static Action GetOnStopped(ConnectionFactory factory, MessageConsumer processors)
        {
            return () => { processors.DeRegister(factory); };
        }
    }
}
