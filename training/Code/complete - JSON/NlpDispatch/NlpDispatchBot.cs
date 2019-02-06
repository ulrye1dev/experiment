﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NLP_With_Dispatch_Bot
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each interaction from the user, an instance of this class is called.
    /// This is a Transient lifetime service. Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single Turn, should be carefully managed.
    /// </summary>
    public class NlpDispatchBot : IBot
    {
        private const string WelcomeText = "This bot will introduce you to Dispatch for QnA Maker and LUIS. Type a greeting, or a question about the weather to get started";

        /// <summary>
        /// Key in the Bot config (.bot file) for the Weather Luis instance.
        /// </summary>
        private const string WeatherLuisKey = "Forecasts";

        /// <summary>
        /// Key in the Bot config (.bot file) for the Dispatch.
        /// </summary>
        private const string DispatchKey = "WeatherDispatch";

        /// <summary>
        /// Key in the Bot config (.bot file) for the QnaMaker instance.
        /// In the .bot file, multiple instances of QnaMaker can be configured.
        /// </summary>
        private const string QnAMakerKey = "weatherbottutorial";

        // API key to access Free OpenWeatherMap APIs.
        // NOTE - Register at http://home.openweathermap.org/users/sign_in to obtain a free subscription key.
        private const string OpenWeatherMapKey = "64ff82cecc338ba76e3a1dc7f19a73ae";

        /// <summary>
        /// Services configured from the ".bot" file.
        /// </summary>
        private readonly BotServices _services;

        /// <summary>
        /// Initializes a new instance of the <see cref="NlpDispatchBot"/> class.
        /// </summary>
        /// <param name="services">Services configured from the ".bot" file.</param>
        public NlpDispatchBot(BotServices services)
        {
            _services = services ?? throw new System.ArgumentNullException(nameof(services));

            if (!_services.QnAServices.ContainsKey(QnAMakerKey))
            {
                throw new System.ArgumentException($"Invalid configuration. Please check your '.bot' file for a QnA service named '{QnAMakerKey}'.");
            }

            if (!_services.LuisServices.ContainsKey(WeatherLuisKey))
            {
                throw new System.ArgumentException($"Invalid configuration. Please check your '.bot' file for a Luis service named '{WeatherLuisKey}'.");
            }
        }

        /// <summary>
        /// Every conversation turn for our NLP Dispatch Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response, with no stateful conversation.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data neededDispatchToTopIntentAsync
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (turnContext.Activity.Type == ActivityTypes.Message && !turnContext.Responded)
            {
                // Get the intent recognition result
                var recognizerResult = await _services.LuisServices[DispatchKey].RecognizeAsync(turnContext, cancellationToken);
                var topIntent = recognizerResult?.GetTopScoringIntent();

                await turnContext.SendActivityAsync($"Returned intent: {topIntent.Value.intent} ({topIntent.Value.score}).");

                // See if LUIS found and used an entity to determine user intent.
                var entityFound = ParseLuisForEntities(recognizerResult);

                if (topIntent == null)
                {
                    await turnContext.SendActivityAsync("Unable to get the top intent.");
                }
                else
                {
                    await DispatchToTopIntentAsync(turnContext, topIntent, cancellationToken);
                }
            }
            else if (turnContext.Activity.Type == ActivityTypes.ConversationUpdate)
            {
                // Send a welcome message to the user and tell them what actions they may perform to use this bot
                if (turnContext.Activity.MembersAdded != null)
                {
                    await SendWelcomeMessageAsync(turnContext, cancellationToken);
                }
            }
            else
            {
                await turnContext.SendActivityAsync($"{turnContext.Activity.Type} event detected", cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// On a conversation update activity sent to the bot, the bot will
        /// send a message to the any new user(s) that were added.
        /// </summary>
        /// <param name="turnContext">Provides the <see cref="ITurnContext"/> for the turn of the bot.</param>
        /// <param name="cancellationToken" >(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>>A <see cref="Task"/> representing the operation result of the Turn operation.</returns>
        private static async Task SendWelcomeMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(
                        $"Welcome to Dispatch bot {member.Name}. {WelcomeText}",
                        cancellationToken: cancellationToken);
                }
            }
        }

        /// <summary>
        /// Depending on the intent from Dispatch, routes to the right LUIS model or QnA service.
        /// </summary>
        private async Task DispatchToTopIntentAsync(ITurnContext context, (string intent, double score)? topIntent, CancellationToken cancellationToken = default(CancellationToken))
        {
            const string dailyForecastDispatchKey = "l_Weather";
            const string noneDispatchKey = "None";
            const string qnaDispatchKey = "q_Weather";

            switch (topIntent.Value.intent)
            {
                case dailyForecastDispatchKey:
                    await DispatchToLuisModelAsync(context, WeatherLuisKey);

                    // Here, you can add code for calling the hypothetical weather service,
                    // passing in any entity information that you need
                    break;
                case noneDispatchKey:
                // You can provide logic here to handle the known None intent (none of the above).
                // In this example we fall through to the QnA intent.
                case qnaDispatchKey:
                    await DispatchToQnAMakerAsync(context, QnAMakerKey);
                    break;

                default:
                    // The intent didn't match any case, so just display the recognition results.
                    await context.SendActivityAsync($"Dispatch intent: {topIntent.Value.intent} ({topIntent.Value.score}).");
                    break;
            }
        }

        /// <summary>
        /// Dispatches the turn to the request QnAMaker app.
        /// </summary>
        private async Task DispatchToQnAMakerAsync(ITurnContext context, string appName, CancellationToken cancellationToken = default(CancellationToken))
        {
            await context.SendActivityAsync($"Sending your request to the QnA Maker {appName} system ...");

            if (!string.IsNullOrEmpty(context.Activity.Text))
            {
                var results = await _services.QnAServices[appName].GetAnswersAsync(context);
                if (results.Any())
                {
                    await context.SendActivityAsync(results.First().Answer, cancellationToken: cancellationToken);
                }
                else
                {
                    await context.SendActivityAsync($"Couldn't find an answer in the {appName}.");
                }
            }
        }

        /// <summary>
        /// Dispatches the turn to the requested LUIS model.
        /// </summary>
        private async Task DispatchToLuisModelAsync(ITurnContext context, string appName, CancellationToken cancellationToken = default(CancellationToken))
        {
            await context.SendActivityAsync($"Sending your request to the {appName} system ...");
            var result = await _services.LuisServices[appName].RecognizeAsync(context, cancellationToken);

            var topIntent = result?.GetTopScoringIntent();

            // See if LUIS found and used an entity to determine user intent.
            var entityFound = ParseLuisForEntities(result);

            if (topIntent != null && entityFound != "" && topIntent.HasValue && topIntent.Value.intent != "None")
            {
                // await context.SendActivityAsync($"==>LUIS Top Scoring Intent: {topIntent.Value.intent}, LUIS location entity: {entityFound}, Score: {topIntent.Value.score}\n");

                if (topIntent.Value.intent == "Daily_Forecast")
                {
                    // Use top intent and "entityFound" = location to call daily weather service here...
                    string dailyURL = "http://api.openweathermap.org/data/2.5/weather?q=" + entityFound + "&APPID=" + OpenWeatherMapKey;
                    var jsonResult = GetFormattedJSON(dailyURL);

                    var currentConditions = FindCurrentConditions(jsonResult);
                    var currentTemp = FindCurrentTemp(jsonResult);
                    await context.SendActivityAsync($"==>LUIS Top Scoring Intent: {topIntent.Value.intent}, LUIS location entity: {entityFound}, Score: {topIntent.Value.score}\n Daily weather forecast for {entityFound}.\n " + currentConditions + ", temperature: " + currentTemp + "F");
                }
                else if (topIntent.Value.intent == "Hourly_Forecast")
                {
                    // Use top intent and "entityFound" = location to call hourly weather service here...
                    string hourlyURL = "http://api.openweathermap.org/data/2.5/forecast?q=" + entityFound + "&APPID=" + OpenWeatherMapKey;
                    var jsonResult = GetFormattedJSON(hourlyURL);

                    // Call FindHourlyForecast
                    var currentForecast = FindHourlyForecast(jsonResult);
                    await context.SendActivityAsync($"==>LUIS Top Scoring Intent: {topIntent.Value.intent}, LUIS location entity: {entityFound}, Score: {topIntent.Value.score}\n Hourly weather forecasts for {entityFound}.\n" + currentForecast);
                }
            }
            else
            {
                var msg = @"No LUIS intents with a location entity were found.
                            This sample is about identifying two user intents:
                            'Daily_Forecast'
                            'Hourly_Forecast'
                            Try typing 'Show me weather for Redmond.' or 'When will it start to rain in Redmond?'.";
                await context.SendActivityAsync(msg);
            }
        }

        /// <summary>
        /// Examines the LUIS response for entities, namely a location for the weather forecast.
        /// </summary>
        /// <param name="recognizerResult">Results from LUIS.</param>
        /// <returns>String containing the entities, if any.</returns>
        private string ParseLuisForEntities(RecognizerResult recognizerResult)
        {
            var result = string.Empty;

            // recognizerResult.Entities returns type JObject.
            foreach (var entity in recognizerResult.Entities)
            {
                // Parse JObject for a known entity types: Appointment, Meeting, and Schedule.
                var locationFound = JObject.Parse(entity.Value.ToString())["location"];

                // We will return info on the first entity found.
                if (locationFound != null)
                {
                    // use JsonConvert to convert entity.Value to a dynamic object.
                    dynamic o = JsonConvert.DeserializeObject<dynamic>(entity.Value.ToString());
                    if (o.location[0] != null)
                    {
                        // Find and return the entity type and score.
                        var entText = o.location[0].text;
                        var entScore = o.location[0].score;
                        result = entText;

                        return result;
                    }
                }
            }

            // No entity results found.
            return result;
        }

        /// <summary>
        /// Gets JSON from weather API and formats it into a JObject.
        /// </summary>
        /// <param name="url">URL for weather API call.</param>
        /// <returns>JObject containing the returned weather information.</returns>
        private JObject GetFormattedJSON(string url)
        {
            // Create a web client.
            using (WebClient client = new WebClient())
            {
                // Get the response string from the URL.
                string resultJSON = client.DownloadString(url);

                JObject json = JObject.Parse(resultJSON);

                return json;
            }
        }

        /// <summary>
        /// Converts from Kelvin to Fahrenheit.
        /// </summary>
        /// <param name="kelvin">Tempurature value, in Kelvin</param>
        /// <returns>String representation of the Fahrenheit tempurature.</returns>
        private string KelvinToFahrenheit(double kelvin)
        {
            string currentTempString = "00.00";

            double tempFahrenheit = (1.8 * (kelvin - 273.15)) + 32;
            currentTempString = Convert.ToString(tempFahrenheit);

            // truncate to xx.xx or -x.xx ...
            currentTempString = currentTempString.Substring(0, 5);

            return currentTempString;
        }

        /// <summary>
        /// Find the current temp from provided JSON forecast.
        /// </summary>
        /// <param name="json">Forecast information from OpenWeather API.</param>
        /// <returns>String representation of the current tempurature.</returns>
        private string FindCurrentTemp(JObject json)
        {
            return KelvinToFahrenheit((double)json["main"]["temp"]);
        }

        /// <summary>
        /// Finds the current conditions from provided forecast information.
        /// </summary>
        /// <param name="json">Forecast information from OpenWeather API.</param>
        /// <returns>String representation of current weather conditions.</returns>
        private string FindCurrentConditions(JObject json)
        {
            string currentConditions = (string)json["weather"][0]["description"];
            string currentSkies = (string)json["weather"][0]["main"];

            // format and return conditions string
            string conditionsString = "Skies: " + currentSkies + ", conditions: " + currentConditions;
            return conditionsString;
        }

        /// <summary>
        /// Finds the hourly forecast from provided forecast information.
        /// </summary>
        /// <param name="json">Forecast information from OpenWeather API.</param>
        /// <returns>String representation of hourly weather conditions.</returns>
        private string FindHourlyForecast(JObject json)
        {
            string hourlyForecastString = string.Empty;
            string hourlyTempString = "00.00";
            string hourlyConditionString = "cloudy";
            string hourlyTimeString = string.Empty;

            int counter = 0;

            // LINQ query to get the list of hourly forecasts
            var hourlyForecast =
                from f in json["list"]
                select f;

            foreach (var forecast in hourlyForecast)
            {
                // Get the temp and convert it.
                hourlyTempString = FindCurrentTemp(forecast as JObject);

                // Get the conditions.
                hourlyConditionString = FindCurrentConditions(forecast as JObject);

                // Get the current time from the forecast.
                DateTime start_time = (DateTime)forecast["dt_txt"];

                // Convert from UTC to local time.
                start_time = start_time.ToLocalTime();

                // Add 90 minutes to get to the middle of the interval.
                start_time += new TimeSpan(1, 30, 0);
                hourlyTimeString = start_time.ToShortTimeString();

                // Build the forecast string from information above and append it.
                hourlyForecastString = hourlyForecastString + "Forecast for: " + hourlyTimeString + ", Temperature: " + hourlyTempString + "F, " + hourlyConditionString + "\n";

                // Only give first 8, which is a full day.
                counter++;
                if (counter > 7)
                {
                    break;
                }
            }

            return hourlyForecastString;
        }
    }
}