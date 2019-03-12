﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Ocdt.DomainModel;
using OCDT_Notifier.Utilities;
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
            EngineeringModelSetup engineeringModelSetup = parameterValueSets[0].ContainerParameter.ContainerElementDefinition.ContainerIteration.ContainerEngineeringModel.EngineeringModelSetup;

            // We assume the list is not empty
            var text = String.Format("#### {0} {1} – Parameter values\n", FormatClassKind(ClassKind.ParameterValueSet), engineeringModelSetup.Name);
            text += "|Domain|Type| Equipment | Parameter | New value | Published |\n|:---:|:---:|-----|:------:|:----|:----|\n";

            foreach (ParameterValueSet parameterValueSet in parameterValueSets) {
                ChangeKind changeKind = metadata[parameterValueSet.Iid].Item1;

                Logger.Trace("Parameter: {}", parameterValueSet);
                Logger.Trace("Param Owner: {}", parameterValueSet.DeriveOwner());
                Logger.Trace("Param Element: {}", parameterValueSet.ContainerParameter.ContainerElementDefinition);
                Logger.Trace("Param Path: {}", parameterValueSet.ContainerParameter.Path);

                text += String.Format("|{0}|{1}|{2}|{3}|**{4}** {5}{6}|{7} {5}|\n",
                    parameterValueSet.DeriveOwner().ShortName,
                    FormatChangeKind(changeKind),
                    parameterValueSet.ContainerParameter.ContainerElementDefinition.Name + " (" + parameterValueSet.ContainerParameter.ContainerElementDefinition.ShortName + ")",
                    parameterValueSet.ContainerParameter.ParameterType.Name,
                    parameterValueSet.ActualValue[0],
                    parameterValueSet.DeriveMeasurementScale() != null ? parameterValueSet.DeriveMeasurementScale().ShortName : "",
                    parameterValueSet.ActualState == null ? "" : " (" + parameterValueSet.ActualState.ShortName + ")",
                    parameterValueSet.Published[0]
                    );
            }

            SendMessage(text, parameterValueSets.First().Owner);
        }

        public void NotifyElementDefinition(List<ElementDefinition> elementDefinitions, Dictionary<Guid, Tuple<ChangeKind>> metadata)
        {
            EngineeringModelSetup engineeringModelSetup = elementDefinitions[0].ContainerIteration.ContainerEngineeringModel.EngineeringModelSetup;

            var text = String.Format("#### {0} {1} – Element Definitions\n", FormatClassKind(ClassKind.ElementDefinition), engineeringModelSetup.Name);
            text += "|Domain|Type| Name | Description | Categories | # Parameters |\n|:---:|:---:|-----|------|:----:|----:|\n";

            foreach (ElementDefinition elementDefinition in elementDefinitions) {
                ChangeKind changeKind = metadata[elementDefinition.Iid].Item1;

                Logger.Trace("Element path: {}", elementDefinition.Path);

                var definition = elementDefinition.Definition.SingleOrDefault(d => d.LanguageCode == "en-GB");
                if (definition == null) definition = elementDefinition.Definition.SingleOrDefault();

                text += String.Format("|{0}|{1}{2}|{3} ({4})|{5}|{6}|{7}|\n",
                   elementDefinition.Owner.ShortName,
                   FormatChangeKind(changeKind),
                   (changeKind != ChangeKind.Updated) ? " **Element " + changeKind.ToString() + "**" : "",
                   elementDefinition.Name,
                   elementDefinition.ShortName,
                   definition == null ? "" : definition.Content,
                   string.Join(", ", elementDefinition.GetAllCategories().Select(c => c.ShortName)),
                   elementDefinition.Parameter.Count
                    );
            }

            SendMessage(text, elementDefinitions.First().Owner);
        }

        public void NotifyElementUsage(List<ElementUsage> elementUsages, Dictionary<Guid, Tuple<ChangeKind>> metadata)
        {
            EngineeringModelSetup engineeringModelSetup = elementUsages[0].ContainerElementDefinition.ContainerIteration.ContainerEngineeringModel.EngineeringModelSetup;

            var text = String.Format("#### {0} {1} – Element Usages\n", FormatClassKind(ClassKind.ElementUsage), engineeringModelSetup.Name);
            text += "|Domain|Type| Parent | Name | Element |\n|:---:|:---:|-----|------|----|\n";

            foreach (ElementUsage elementUsage in elementUsages) {
                ChangeKind changeKind = metadata[elementUsage.Iid].Item1;

                Logger.Trace("Element usage path: {}", elementUsage.Path);

                text += String.Format("|{0}|{1}{2}|{3}|{4} ({5})|{5} ({6})\n",
                   elementUsage.Owner.ShortName,
                   FormatChangeKind(changeKind),
                   (changeKind != ChangeKind.Updated) ? " **Usage " + changeKind.ToString() + "**" : "",
                   elementUsage.ContainerElementDefinition.Name,
                   elementUsage.Name,
                   elementUsage.ShortName,
                   elementUsage.ElementDefinition.Name,
                   elementUsage.ElementDefinition.ShortName
                );
            }

            SendMessage(text, elementUsages.First().Owner);
        }

        public void NotifyPublication(Publication publication, Dictionary<Guid, Tuple<ChangeKind>> metadata)
        {
            EngineeringModelSetup engineeringModelSetup = publication.ContainerIteration.ContainerEngineeringModel.EngineeringModelSetup;

            var text = String.Format("{0}\n#### {1} {2} – Publication\n",
                OCDTNotifier.configuration.Target.PublicationPreamble,
                FormatClassKind(ClassKind.Publication),
                engineeringModelSetup.Name
            );
            text += String.Format("Publication #**{0}**, {1}.\nContains {2} parameter{3} from {4}.\n\n",
                publication.PublicationNumber,
                publication.CreatedOn,
                publication.PublishedParameter.Count,
                publication.PublishedParameter.Count == 1 ? "" : "s",
                publication.Domain.Aggregate("", (s, v) => s += ((s == "") ? "" : ", ") + v.ShortName)
            );

            text += "|DOE|Typ.| Element | Parameter | Parameter Path | Published Value | Switch |\n|:---:|:---:|-----|------|----|---|:---:|\n";

            ThingTreeNode publicationTree = new ThingTreeNode(null);

            foreach (ParameterOrOverrideBase paramBase in publication.PublishedParameter) {
                ElementDefinition elementDefinition = paramBase.GetContainerElementDefinition();

                if (paramBase.GetType() == typeof(Ocdt.DomainModel.Parameter)) {
                    Ocdt.DomainModel.Parameter parameter = (Ocdt.DomainModel.Parameter)paramBase;

                    foreach (ParameterValueSet valueSet in parameter.ValueSet) {
                        publicationTree.AddThingAndItsContainers(valueSet);

                        text += String.Format("|{0}|{1}|**{2}**|**{3}**| `{4}` |**{5}** {6}|{7}|\n",
                           parameter.Owner.ShortName,
                           FormatClassKind(valueSet),
                           elementDefinition.Name,
                           parameter.ParameterType.Name,
                           valueSet.Path,
                           valueSet.Published.First(),
                           valueSet.DeriveMeasurementScale().ShortName,
                           valueSet.ValueSwitch
                        );
                    }
                }
            }


            SendMessage(text);
        }

        public void NotifyIteration(List<Iteration> iterations, Dictionary<Guid, Tuple<ChangeKind>> metadata)
        {
            EngineeringModelSetup engineeringModelSetup = iterations.First().ContainerEngineeringModel.EngineeringModelSetup;

            var text = String.Format("#### {0} {1} – Iterations\n", FormatClassKind(ClassKind.Iteration), engineeringModelSetup.Name);
            text += "|Type| Engineering Model | Iteration # | Description | Frozen on |\n|:---:|---|-----|:---|----|\n";

            foreach (Iteration iteration in iterations) {
                ChangeKind changeKind = metadata[iteration.Iid].Item1;

                text += String.Format("|{0}{1}|{2} ({3})|**{4}**|{5}|\n",
                   FormatChangeKind(changeKind),
                   (changeKind != ChangeKind.Updated) ? " **Iteration " + changeKind.ToString() + "**" : "",
                   iteration.ContainerEngineeringModel.EngineeringModelSetup.Name,
                   iteration.ContainerEngineeringModel.EngineeringModelSetup.ShortName,
                   iteration.IterationSetup.IterationNumber,
                   iteration.IterationSetup.Description,
                   iteration.IterationSetup.FrozenOn
                );
            }

            SendMessage(text);
        }


        public void NotifyOther(Thing thing)
        {
            var text = String.Format("##### Other change ({0})\n`{1}`\n```yaml\n{2}```",
                thing.GetType().Name,
                thing.ToShortString(),
                thing.ToDto().ToJsonString().Replace(",", ",\n")
                );

            SendMessage(text);
        }

        private String FormatClassKind(Thing thing)
        {
            if (thing.ClassKind == ClassKind.ParameterValueSet) {
                if (((ParameterValueSet)thing).ContainerParameter.IsStateDependent) {
                    return ":gear::hammer_and_pick:";
                }
            }

            return FormatClassKind(thing.ClassKind);
        }

        private String FormatClassKind(ClassKind classKind)
        {
            switch(classKind) {
            case ClassKind.ParameterValueSet:
                return ":gear:";
            case ClassKind.ElementDefinition:
                return ":package:";
            case ClassKind.ElementUsage:
                return ":arrow_lower_right: :package:";
            default:
                return "";
            }
        }

        private String FormatChangeKind(ChangeKind changeKind)
        {
            switch(changeKind) {
            case ChangeKind.Added:
                return ":sparkle:";
            case ChangeKind.Updated:
                return ":large_blue_circle:";
            case ChangeKind.Deleted:
                return ":x:";
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
        private void SendMessage(String message, DomainOfExpertise domain = null)
        {
            SendCustomMessage (new {
                text = message,
                icon_url = GetDomainImage(domain)
            });
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

        /// <summary>
        /// Get the profile image corresponding to a certain domain of expertise
        /// </summary>
        /// <param name="domainOfExpertise">A domain of expertise, or null</param>
        /// <returns>The URL of the image, or "" on failure</returns>
        private String GetDomainImage(DomainOfExpertise domainOfExpertise)
        {
            if (domainOfExpertise == null) {
                return "";
            } else if (!OCDTNotifier.configuration.Output.SplitOwners) {
                // If we're not splitting owners, don't show an image
                return "";
            } else if (OCDTNotifier.configuration.Target.ProfileIcons.ContainsKey(domainOfExpertise.ShortName)) {
                return OCDTNotifier.configuration.Target.ProfileIcons[domainOfExpertise.ShortName];
            } else {
                return "";
            }
        }

        public MattermostTarget()
        {
            hook = new Uri (OCDTNotifier.configuration.Target.Hook);

            client = new RestClient(hook.GetLeftPart(UriPartial.Authority));

            if (OCDTNotifier.configuration.Debug) {
                SendMessage ("```DEBUG: MM Target initialised```");
            }
        }
    }
}
