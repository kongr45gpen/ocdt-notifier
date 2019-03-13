using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Ocdt.DomainModel;
using OCDT_Notifier.Utilities;
using RestSharp;
using Parameter = Ocdt.DomainModel.Parameter;

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
            text += "|Domain|Type| Equipment  | Parameter | New value | Published |\n|:---:|:---:|---|:------:|:----|:----|\n";

            foreach (ParameterValueSet parameterValueSet in parameterValueSets) {
                ChangeKind changeKind = metadata[parameterValueSet.Iid].Item1;

                Logger.Trace("Parameter: {}", parameterValueSet);
                Logger.Trace("Param Owner: {}", parameterValueSet.DeriveOwner());
                Logger.Trace("Param Element: {}", parameterValueSet.ContainerParameter.ContainerElementDefinition);
                Logger.Trace("Param Path: {}", parameterValueSet.ContainerParameter.Path);

                text += String.Format("|{0}|{1}|**{2}** ({10})|**{3}**{9}|**{4}** {5}{6}|{7} {5}|\n",
                    parameterValueSet.DeriveOwner().ShortName,
                    FormatChangeKind(changeKind),
                    parameterValueSet.ContainerParameter.ContainerElementDefinition.Name,
                    parameterValueSet.ContainerParameter.ParameterType.Name,
                    parameterValueSet.ActualValue[0],
                    parameterValueSet.DeriveMeasurementScale() != null ? parameterValueSet.DeriveMeasurementScale().ShortName : "",
                    parameterValueSet.ActualState == null ? "" : " (" + parameterValueSet.ActualState.ShortName + ")",
                    parameterValueSet.Published[0],
                    parameterValueSet.ContainerParameter.GetParameterGroupPath(), // We don't actually show the group. It is shown on the parameter.
                    parameterValueSet.ActualOption == null ? "" : " (" + parameterValueSet.ActualOption.ShortName + ")",
                    parameterValueSet.ContainerParameter.ContainerElementDefinition.ShortName
                    );
            }

            SendMessage(text, parameterValueSets.First().Owner);
        }

        public void NotifyParameter(List<Parameter> parameters, Dictionary<Guid, Tuple<ChangeKind>> metadata)
        {
            EngineeringModelSetup engineeringModelSetup = parameters[0].ContainerElementDefinition.ContainerIteration.ContainerEngineeringModel.EngineeringModelSetup;

            // We assume the list is not empty
            var text = String.Format("#### {0} {1} – Parameters\n", FormatClassKind(ClassKind.Parameter), engineeringModelSetup.Name);
            text += "|Domain|Type| Equipment  | Group path | Parameter | StD | OptD | Scale |\n|:---:|:---:|---|:---|:---|:---:|:---:|:---|\n";

            foreach (Parameter parameter in parameters) {
                ChangeKind changeKind = metadata[parameter.Iid].Item1;

                text += String.Format("|{0}|{1}|**{2}** ({3})|`{4}`|**{5}** ({6})|{9}|{10}|{7} ({8})|\n",
                    parameter.Owner.ShortName,
                    FormatChangeKind(changeKind),
                    parameter.ContainerElementDefinition.Name,
                    parameter.ContainerElementDefinition.ShortName,
                    parameter.Group != null ? parameter.GetParameterGroupPath() : " ",
                    parameter.ParameterType.Name,
                    parameter.ParameterType.ShortName,
                    parameter.Scale.Name,
                    parameter.Scale.ShortName,
                    parameter.IsStateDependent ? parameter.StateDependence.Name : "",
                    parameter.IsOptionDependent ? "✓" : ""
                    );
            }

            SendMessage(text, parameters.First().Owner);
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

            if (OCDTNotifier.configuration.Target.DisplayTrees) {
                ThingTreeNode publicationTree = new ThingTreeNode(null);

                foreach (ParameterOrOverrideBase paramBase in publication.PublishedParameter) {
                    ElementDefinition elementDefinition = paramBase.GetContainerElementDefinition();

                    if (paramBase.ClassKind == ClassKind.Parameter) {
                        foreach (ParameterValueSet t in ((Parameter)paramBase).ValueSet) {
                            publicationTree.AddThingAndItsContainers(t);
                        }
                    } else if (paramBase.ClassKind == ClassKind.ParameterOverride) {
                        foreach (ParameterOverrideValueSet t in ((ParameterOverride)paramBase).ValueSet) {
                            publicationTree.AddThingAndItsContainers(t);
                        }
                    }
                }

                text += DesignFullTree(publicationTree);
            } else {
                // Displaying clean values
                text += "|DOE|Typ.| Element | Parameter | State | Published Value | Switch |\n";
                text += "|:---:|:---:|-----|------|----|---|:---:|\n";

                foreach (ParameterOrOverrideBase paramBase in publication.PublishedParameter) {
                    // Are we Parameter or ParameterOverride?
                    if (paramBase.ClassKind == ClassKind.Parameter) {
                        Parameter parameter = (Parameter)paramBase;
                        foreach (ParameterValueSet parameterValueSet in parameter.ValueSet) {
                            text += String.Format("|{0}|{1}|{2}|**{3}**| **{4}** |**{5}** {6}|{7}|\n",
                                  parameterValueSet.Owner.ShortName,
                                  FormatClassKind(parameterValueSet),
                                  parameter.ContainerElementDefinition.Name,
                                  parameter.ParameterType.Name,
                                  FormatDependencies(parameterValueSet),
                                  parameterValueSet.Published.First(),
                                  parameterValueSet.DeriveMeasurementScale()?.ShortName,
                                  parameterValueSet.ValueSwitch
                               );
                        }
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

        /// <summary>
        /// Draws the full tree of a list of ElementBases in Markdown.
        /// </summary>
        /// <param name="rootNode">The root node of a tree containing <see cref="ElementBase"/>s.</param>
        /// <returns>A markdown table with its heading.</returns>
        protected String DesignFullTree(ThingTreeNode rootNode)
        {
            // The table heading
            String text = "|DOE|Typ.| Element | Parameter | State | Published Value | Switch |\n";
            text += "|:---:|:---:|-----|------|----|---|:---:|\n";

            rootNode.Traverse(thing => {
                if (thing.ClassKind == ClassKind.ParameterValueSet) {
                    ParameterValueSet parameterValueSet = (ParameterValueSet)thing;
                    Parameter parameter = parameterValueSet.ContainerParameter;

                    text += String.Format("|{0}|{1}||**{3}**| **{4}** |**{5}** {6}|{7}|\n",
                          parameterValueSet.Owner.ShortName,
                          FormatClassKind(thing),
                          parameter.ContainerElementDefinition.Name,
                          parameter.IsStateDependent ? " " : parameter.ParameterType.Name,
                          FormatDependencies(parameterValueSet),
                          parameterValueSet.Published.First(),
                          parameterValueSet.DeriveMeasurementScale()?.ShortName,
                          parameterValueSet.ValueSwitch
                       );
                } else if (thing.ClassKind == ClassKind.Parameter) {
                    Parameter parameter = (Parameter)thing;

                    // We don't want to bloat double entries with non-state-dependent parameters
                    if (!parameter.IsStateDependent && !parameter.IsOptionDependent) {
                        return;
                    }

                    text += String.Format("|{0}|{1}||**{3}**|{4}|||\n",
                          parameter.Owner.ShortName,
                          FormatClassKind(thing),
                          parameter.ContainerElementDefinition.Name,
                          parameter.ParameterType.Name,
                          FormatDependencies(parameter)
                       );
                } else if (thing.ClassKind == ClassKind.ElementDefinition) {
                    ElementDefinition elementDefinition = (ElementDefinition)thing;

                    text += String.Format("||||||||\n|{0}|{1}|**{2}**|||||\n",
                          elementDefinition.Owner.ShortName,
                          FormatClassKind(thing),
                          elementDefinition.Name
                       );
                } else if (thing.ClassKind == ClassKind.ParameterGroup) {
                    ParameterGroup parameterGroup = (ParameterGroup)thing;

                    text += String.Format("|{0}|{1}||{2}||||\n",
                          "", // Parameter groups don't have Owners
                          FormatClassKind(thing),
                          parameterGroup.Name
                       );
                } else {
                    Logger.Warn("Found unexpected container {} while traversing tree", thing.ClassKind);
                }
            });

            return text;
        }

        protected String FormatDependencies(Parameter parameter)
        {
            if (parameter.IsOptionDependent && parameter.IsStateDependent) {
                return String.Format("{2}  {0}",
                    parameter.StateDependence.Name,
                    parameter.StateDependence.ShortName,
                    FormatClassKind(ClassKind.Option)
                );
            } else if (parameter.IsStateDependent) {
                return String.Format("{0}",
                    parameter.StateDependence.Name,
                    parameter.StateDependence.ShortName
                );
            } else if (parameter.IsOptionDependent) {
                return FormatClassKind(ClassKind.Option);
            } else {
                return " ";
            }
        }

        protected String FormatDependencies(ParameterValueSet parameterValueSet)
        {
            Parameter parameter = parameterValueSet.ContainerParameter;

            if (parameter.IsOptionDependent && parameter.IsStateDependent) {
                return String.Format("{0}, {1}",
                    parameterValueSet.ActualState.Name,
                    parameterValueSet.ActualOption.Name
                );
            } else if (parameter.IsStateDependent) {
                return String.Format("{0}",
                    parameterValueSet.ActualState.Name
                );
            } else if (parameter.IsOptionDependent) {
                return String.Format("{0}",
                    parameterValueSet.ActualOption.Name
                );
            } else {
                return " ";
            }
        }

        /// <summary>
        /// Return a prettified Markdown string corresponding to the provided Thing, and
        /// mostly its <see cref="ClassKind"/>.
        /// Usually an emoji or an image.
        /// </summary>
        /// <param name="thing"></param>
        /// <returns></returns>
        protected String FormatClassKind(Thing thing)
        {
            if (thing.ClassKind == ClassKind.ParameterValueSet) {
                // Add extra icons for state- and option-dependent parameters
                var ret = ":gear:";
                if (((ParameterValueSet)thing).ContainerParameter.IsStateDependent) {
                    ret += ":hammer_and_pick:";
                }
                if (((ParameterValueSet)thing).ContainerParameter.IsOptionDependent) {
                    ret += ":control_knobs:";
                }
                return ret;
            }

            return FormatClassKind(thing.ClassKind);
        }

        /// <summary>
        /// Return a prettified Markdown string corresponding to the provided ClassKind.
        /// Usually an emoji or an image.
        /// </summary>
        /// <param name="thing"></param>
        /// <returns></returns>
        protected String FormatClassKind(ClassKind classKind)
        {
            switch(classKind) {
                case ClassKind.Parameter:
                    return ":level_slider:";
                case ClassKind.ParameterValueSet:
                    return ":gear:";
                case ClassKind.ElementDefinition:
                    return ":package:";
                case ClassKind.ParameterGroup:
                    return ":open_file_folder:";
                case ClassKind.Option:
                    return ":control_knobs:";
                case ClassKind.ElementUsage:
                    return ":arrow_lower_right: :package:";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Return a prettified Markdown string corresponding to the provided ChangeKind.
        /// Usually an emoji or an image.
        /// </summary>
        /// <param name="thing"></param>
        /// <returns></returns>
        protected String FormatChangeKind(ChangeKind changeKind)
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
        protected void SendMessage(String message, DomainOfExpertise domain = null)
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
        protected String GetDomainImage(DomainOfExpertise domainOfExpertise)
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
