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

namespace ControllerService.Processors
{
    public class RequestProcessor{
        public async System.Threading.Tasks.Task ProcessResponseAsync(HttpContext context, IApplicationBuilder app, ConnectionFactory factory)
        {
            try
                {
                    if(context.Request.Method == "OPTIONS")
                    {
                        context.Response.Headers.Add("Access-Control-Allow-Origin","*");
                        context.Response.Headers.Add("Access-Control-Allow-Methods",new[] {HttpMethods.Post, HttpMethods.Get, HttpMethods.Options, HttpMethods.Put, HttpMethods.Trace, HttpMethods.Delete});
                        return;
                    }
                    var requestPath = context.Request.Path.Value;
                    Console.WriteLine($"Request Path {requestPath} and with method {context.Request.Method}");

                    if(string.IsNullOrEmpty(requestPath)
                    || requestPath == "/" || requestPath =="/favicon.ico" || requestPath =="/cloudfoundryapplication" )
                    {
                        await context.Response.WriteAsync("Running!!");
                        return;
                    }

                    if( requestPath == "/config")
                    {
                        var environments = Environment.GetEnvironmentVariables();
                        XElement data1 = new XElement("parent");
                        var data = "";

                        foreach(var val in environments.Keys)
                        {
                            try
                            {
                            data += val.ToString() +":" + Environment.GetEnvironmentVariable(val.ToString()) + "||";
                            //data1.Add(new XElement(val.ToString(), Environment.GetEnvironmentVariable(val.ToString())));
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine(ex);
                            }
                            
                           
                        }
                        await context.Response.WriteAsync(data.ToString());
                        return;

                        
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
                    headers.Add("Method", context.Request.Method);

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

                    Console.WriteLine($"Data is being published {serializedMessage}");

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
            
        }
        
        private static Task<string> ConvertToString(Stream stream)
        {
            var rdr =  new StreamReader(stream);

            return rdr.ReadToEndAsync();
        }
    }
}