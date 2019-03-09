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

        public void NotifyParameterValueSet(List<ParameterValueSet> parameterValueSets, Dictionary<Guid, Tuple<ChangeKind>> metadata)
        {
            EngineeringModelSetup engineeringModelSetup = parameterValueSets [0].ContainerParameter.ContainerElementDefinition.ContainerIteration.ContainerEngineeringModel.EngineeringModelSetup;

            // We assume the list is not empty
            var text = String.Format ("#### {0} – Parameter values", engineeringModelSetup.Name);
            text += "|Domain|Type| Equipment | Parameter | New value | Published |\n|:---:|:---:|-----|------|:----:|:----:|\n";

            foreach (ParameterValueSet parameterValueSet in parameterValueSets) {
                ChangeKind changeKind = metadata [parameterValueSet.Iid].Item1;

                Logger.Trace ("Parameter: {}", parameterValueSet);
                Logger.Trace ("Param Owner: {}", parameterValueSet.DeriveOwner ());
                Logger.Trace ("Param Element: {}", parameterValueSet.ContainerParameter.ContainerElementDefinition);

                text += String.Format ("|{0}|{1}|{2}|{3}|**{4}** {5}|{6} {5}|\n",
                    parameterValueSet.DeriveOwner().ShortName,
                    FormatChangeKind(changeKind),
                    parameterValueSet.ContainerParameter.ContainerElementDefinition.Name + " (" + parameterValueSet.ContainerParameter.ContainerElementDefinition.ShortName + ")",
                    parameterValueSet.ContainerParameter.ParameterType.Name,
                    parameterValueSet.ActualValue [0],
                    parameterValueSet.DeriveMeasurementScale () != null ? parameterValueSet.DeriveMeasurementScale ().ShortName : "",
                    parameterValueSet.Published[0]
                    );

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

        private String FormatChangeKind(ChangeKind changeKind)
        {
            switch(changeKind) {
            case ChangeKind.Added:
                return ":sparkle:";
            case ChangeKind.Updated:
                return ":large_blue_circle:";
            case ChangeKind.Deleted:
                return ":heavy_minus_sign:";
            case ChangeKind.Conflicted:
                return ":warning:";
            default:
                return ":question:";
            }
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
