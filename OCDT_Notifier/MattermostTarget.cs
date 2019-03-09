using System;
using System.Collections.Generic;
using NLog;
using Ocdt.DomainModel;
using RestSharp;

namespace OCDT_Notifier
{
    public class MattermostTarget
    {
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        protected RestClient client;

        protected Uri hook;

        public void NotifyParameterValueSet(List<ParameterValueSet> parameterValueSets)
        {
            // We assume the list is not empty
            var text = "| Equipment | Parameter | Published | New value |\n| ----- |------|:----:|:----:|\n";

            foreach (ParameterValueSet parameterValueSet in parameterValueSets) {
                Logger.Trace ("Parameter: {}", parameterValueSet);
                Logger.Trace ("Param Owner: {}", parameterValueSet.DeriveOwner ());
                Logger.Trace ("Param Element: {}", parameterValueSet.ContainerParameter.ContainerElementDefinition);

                text += String.Format ("|{0}|{1}|{2} {3}|**{4}** {3}|\n",
                    parameterValueSet.ContainerParameter.ContainerElementDefinition.Name + " (" + parameterValueSet.ContainerParameter.ContainerElementDefinition.ShortName + ")",
                    parameterValueSet.ContainerParameter.ParameterType.Name,
                    parameterValueSet.Published[0],
                    parameterValueSet.DeriveMeasurementScale () != null ? parameterValueSet.DeriveMeasurementScale ().ShortName : "",
                    parameterValueSet.ActualValue[0]
                    );

                //var text = String.Format ("##### {0} Parameter value change\n- Equipment: **{1}** - {2}\n- New value: **{3}** {4} ({5})\n- Published (old) value: {6}",
                    //parameterValueSet.DeriveOwner ().ShortName,
                    //parameterValueSet.ContainerParameter.ContainerElementDefinition.Name + " (" + parameterValueSet.ContainerParameter.ContainerElementDefinition.ShortName + ")",
                    //parameterValueSet.ContainerParameter.ParameterType.Name,
                    //parameterValueSet.ActualValue [0],
                    //parameterValueSet.DeriveMeasurementScale () != null ? parameterValueSet.DeriveMeasurementScale ().ShortName : "",
                    //parameterValueSet.ValueSwitch.ToString (),
                    //parameterValueSet.Published [0]
                    //);

                SendMessage (text);
            }
        }

        public void NotifyOther(Thing thing)
        {
            var text = String.Format ("##### Other change ({0})\n`{1}`\n```yaml\n{2}```",
                thing.GetType().Name,
                thing.ToShortString(),
                thing.ToDto ().ToJsonString ().Replace (",", ",\n")
                );

            SendMessage (text);
        }

        /// <summary>
        /// Sends a post to the Mattermost channel
        /// </summary>
        /// <param name="message">The markdown-formatted message to send</param>
        private void SendMessage(String message)
        {
            SendCustomMessage (new { text = message });
        }

        /// <summary>
        /// Send a custom request to the MM hook
        /// </summary>
        /// <param name="body">The JSON body of the request</param>
        private void SendCustomMessage(object body)
        {
            Logger.Debug ("Starting MM request...");

            var request = new RestRequest (hook.PathAndQuery);
            request.AddJsonBody (body);
            client.PostAsync (request, (response, handle) => {
                if (response.StatusCode != System.Net.HttpStatusCode.OK) {
                    Logger.Error ("Unable to write to MM: {}", response.Content);
                }
            });
        }

        public MattermostTarget()
        {
            hook = new Uri (OCDTNotifier.configuration.Target.Hook);

            client = new RestClient(hook.GetLeftPart(UriPartial.Authority));

            SendMessage ("```DEBUG: MM Target initialised```");
        }
    }
}
