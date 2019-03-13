using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Binder;
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Ocdt.DataStoreServices;
using Ocdt.DomainModel;
using System.Xml.Serialization;
using System.IO;

namespace OCDT_Notifier {
    public class OCDTNotifier : OcdtToolBase {
        /// <summary>
        /// The logger.
        /// </summary>
        protected static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The configuration, as specified by the user.
        /// </summary>
        public static Configuration configuration = new Configuration();

        protected MattermostTarget target;

        /// <summary>
        /// The list of OCDT engineering models
        /// </summary>
        List<EngineeringModelSetup> engineeringModelSetups;

        /// <summary>
        /// List of the latest checked revisions for all OCDT things
        /// </summary>
        private Dictionary<Guid, int> allRevisions = new Dictionary<Guid, int>();

        /// <summary>
        /// The list of the latest parsed revision for every Engineering Model
        /// </summary>
        private Dictionary<Guid, int> modelRevisions = new Dictionary<Guid, int>();

        /// <summary>
        /// A list of older Thing copies, so that a comparison can be made
        /// </summary>
        private Dictionary<Guid, Thing> olderThings = new Dictionary<Guid, Thing>();

        /// <summary>
        /// The <see cref="ClassKind"/>s that can be cluttered and will be only shown when a
        /// significant update (<see cref="interestingParameters"/>) has occurred to them.
        /// Only works as long as the <see cref="Configuration.OutputType.ClearClutter"/> option
        /// is true.
        /// </summary>
        private static ClassKind[] clutteredKinds = new ClassKind[] {
            ClassKind.ElementDefinition,
            ClassKind.ElementUsage,
            ClassKind.EngineeringModel,
            ClassKind.Iteration
        };

        // TODO: Add others, short name, categories, element definition
        /// <summary>
        /// Significant parameters for <see cref="clutteredKinds"/>
        /// </summary>
        private static String[] interestingParameters = new string[] {
            "name", "definition", "owner", "shortName", "category"
        };

        public OCDTNotifier ()
        {
            // Log onto the communications platform
            target = new MattermostTarget ();

            Logger.Info ("Connecting to OCDT...");

            OpenSession (configuration.Server.Url, configuration.Server.Username, configuration.Server.Password);
            engineeringModelSetups = GetEngineeringModelSetups (configuration.Fetch.Models);

            foreach (var engineeringModelSetup in engineeringModelSetups) {
                initialPoll (engineeringModelSetup);
            }

            while (true) {
                foreach (var engineeringModelSetup in engineeringModelSetups) {
                    poll (engineeringModelSetup);
                }

                if (configuration.Debug) {
                    Console.WriteLine ("Please press Enter to read new values...");
                    Console.ReadLine ();
                } else {
                    // Sleep until the next reading time
                    var interval = (int)(configuration.Fetch.Interval * 1000);
                    Logger.Trace ("Sleeping for {} ms", interval);
                    System.Threading.Thread.Sleep (interval);
                }
            }
        }

        /// <summary>
        /// Perform the first poll on the model, so as to get all the reference
        /// data and load the revisions without notifying the target.
        /// </summary>
        /// <param name="engineeringModelSetup">Engineering model setup.</param>
        private void initialPoll (EngineeringModelSetup engineeringModelSetup)
        {
            Logger.Debug ("Found EngineeringModelSetup {}", engineeringModelSetup.Name);
            var engineeringModel = new EngineeringModel (iid: engineeringModelSetup.EngineeringModelIid);
            var iteration = new Iteration (iid: engineeringModelSetup.LastIterationSetup.IterationIid);
            engineeringModel.Iteration.Add (iteration);

            var queryParameters = new QueryParameters {
                Extent = ExtentQueryParameterKind.DEEP,
                IncludeAllContainers = true,
                // Make sure we include the large reference data for our first
                // read.
                IncludeReferenceData = true
            };

            // Read the iteration
            var domainObjectStoreChange = WebServiceClient.Read (iteration, queryParameters);

            if (domainObjectStoreChange != null) {
                // For every Thing...
                foreach (var changedDomainObject in domainObjectStoreChange.ChangedDomainObjects) {
                    var thing = changedDomainObject.Key;

                    // Add the thing's revision to the revision list
                    allRevisions [thing.Iid] = thing.RevisionNumber;

                    // Store some older Things
                    if (clutteredKinds.Contains(thing.ClassKind)) {
                        // A shallow clone is enough to store underlying
                        // lists as well
                        olderThings[thing.Iid] = thing.CreateShallowClone();
                    }
                }
            }

            // Read the engineering model
            WebServiceClient.Read (engineeringModel);
            engineeringModel = (EngineeringModel)ObjStore.GetByIid (iid: engineeringModel.Iid);
            iteration = (Iteration)ObjStore.GetByIid(iid: engineeringModel.EngineeringModelSetup.LastIterationSetup.IterationIid);
            Logger.Trace ("Loaded EM {} with revision {} (setup revision {})", engineeringModelSetup.ShortName, engineeringModel.RevisionNumber, engineeringModelSetup.RevisionNumber);
            // TODO: These lines may not be needed
            modelRevisions [engineeringModel.Iid] = engineeringModel.RevisionNumber;
            modelRevisions[iteration.Iid] = iteration.RevisionNumber;
        }

        private void poll (EngineeringModelSetup engineeringModelSetup)
        {
            Logger.Debug ("Querying EngineeringModelSetup {}", engineeringModelSetup.Name);
            var engineeringModel = (EngineeringModel)ObjStore.GetByIid (iid: engineeringModelSetup.EngineeringModelIid);
            var iteration = (Iteration)ObjStore.GetByIid(iid: engineeringModel.EngineeringModelSetup.LastIterationSetup.IterationIid);
            //engineeringModel.Iteration.Add (iteration);

            var queryParameters = new QueryParameters {
                Extent = ExtentQueryParameterKind.DEEP,
                IncludeAllContainers = true,
                // Don't include the already loaded and rarely changing reference data
                IncludeReferenceData = false,
                // Make sure we read objects more recent than the last revision
                RevisionNumber = modelRevisions[engineeringModel.Iid]
            };

            try {
                // Read the revision
                var domainObjectStoreChange = WebServiceClient.Read (iteration, queryParameters);

                Logger.Debug (
                    "Read result contains {0} changed domain objects",
                    domainObjectStoreChange == null ? 0 : domainObjectStoreChange.ChangedDomainObjects.Count);

                if (domainObjectStoreChange != null) {
                    // Create a dictionary of thing types, so we can send them to the target later
                    var updatedThings = new Dictionary<ClassKind, List<Thing>> ();
                    // Dictionary of complementary data
                    var metadata = new Dictionary<Guid, Tuple<ChangeKind>> ();

                    /// <summary>
                    /// A list of published <see cref="Parameter"/>s for this update, so that they are not included
                    /// afterwards.
                    /// </summary>
                    SmartSet<Guid> publishedParameters = new SmartSet<Guid>();

                    // For every thing...
                    foreach (var changedDomainObject in domainObjectStoreChange.ChangedDomainObjects) {
                        // Some convenience variables
                        ChangeKind changeKind = changedDomainObject.Value;
                        Thing thing = changedDomainObject.Key;

                        // Make sure that the thing has been updated
                        Logger.Warn ("New update for {}", thing);
                        allRevisions [thing.Iid] = thing.RevisionNumber;

                        // First, find out if we should notify about the thing
                        bool addThingToList = false;
                        if (configuration.Output.ClearClutter) {
                            // Clear clutter is enabled, take attention
                            if (changeKind != ChangeKind.Updated) {
                                // All "significant" changes pass
                                addThingToList = true;
                            } else if (clutteredKinds.Contains(thing.ClassKind)) {
                                // Perform a comparison to see if the thing should be published
                                if (!olderThings.ContainsKey(thing.Iid)) {
                                    // No "old thing" exists; probably new
                                    addThingToList = true;
                                } else {
                                    NativeDtoChangeSet changeSet = new NativeDtoChangeSet();
                                    thing.DeriveNetChanges(olderThings[thing.Iid], changeSet);

                                    // Find only "interesting" changes
                                    addThingToList = changeSet.UpdateSection.FirstOrDefault().Value.Any(u => {
                                        if (interestingParameters.Contains(u.Key)) {
                                            Logger.Trace("Thing contains interesting parameter {}", u.Key);
                                            return true;
                                        } else {
                                            return false;
                                        }
                                    });
                                }
                            } else {
                                addThingToList = true;
                            }
                        } else {
                            addThingToList = true;
                        }

                        // Add the Thing to the list, so it can be sent to the target
                        if (addThingToList) {
                            if (!updatedThings.ContainsKey(thing.ClassKind)) {
                                updatedThings[thing.ClassKind] = new List<Thing>();
                            }
                            updatedThings[thing.ClassKind].Add(thing);
                        }

                        // Add the complementary data of the Thing
                        metadata [thing.Iid] = new Tuple<ChangeKind> (changeKind);

                        // Add the published parameters of this publication
                        if (thing.ClassKind == ClassKind.Publication) {
                            publishedParameters.AddAll(((Publication)thing).PublishedParameter.Select(p => p.Iid));
                        }

                        // Store some older things
                        if (thing.ClassKind == ClassKind.ElementDefinition || thing.ClassKind == ClassKind.ElementUsage) {
                            olderThings[thing.Iid] = thing.CreateShallowClone();
                        }
                    }

                    // Send the things to the target
                    foreach (KeyValuePair<ClassKind, List<Thing>> entry in updatedThings) {
                        Logger.Info ("Detected {} change", entry.Key);

                        switch (entry.Key) {
                        case ClassKind.ParameterValueSet: {
                                // A parameter value was set
                                List<ParameterValueSet> list = entry.Value.ConvertAll (x => (ParameterValueSet)x);
                                // Group by domain of expertise, send a different message for each domain
                                foreach (var sublist in SplitDomainsOfExpertise (list, u => u.Owner)) {
                                    var newSublist = sublist;

                                    // Clear the clutter
                                    if (configuration.Output.ClearClutter) {
                                       // Filter some parameters
                                        newSublist = sublist.FindAll (s => {
                                            // The Guid of the corresponding Element Definition
                                            var element = s.ContainerParameter.ContainerElementDefinition.Iid;

                                            // Don't show parameter changes after their publication
                                            if (s.ActualValue.ContainsSameItemsAs(s.Published)) {
                                                if (publishedParameters.Contains(s.ContainerParameter.Iid)) {
                                                    // The parameter has been published; Don't show it.
                                                    return false;
                                                }
                                            }

                                            if (!metadata.ContainsKey (element)) return true;
                                            // Don't show parameters that correspond to a new or a deleted element
                                            // definition
                                            return metadata [element].Item1 == ChangeKind.Updated;
                                        });
                                    }

                                    if (!newSublist.IsEmpty ()) { // We might have no parameters left
                                        target.NotifyParameterValueSet (newSublist, metadata);
                                    }
                                }
                                break;
                            }
                        case ClassKind.ElementDefinition: {
                                // An element definition was edited
                                List<ElementDefinition> list = entry.Value.ConvertAll (x => (ElementDefinition)x);

                                foreach (var sublist in SplitDomainsOfExpertise (list, u => u.Owner)) {
                                    target.NotifyElementDefinition (sublist, metadata);
                                }

                                break;
                            }
                        case ClassKind.ElementUsage: {
                                // An element usage was edited
                                List<ElementUsage> list = entry.Value.ConvertAll(x => (ElementUsage)x);

                                foreach (var sublist in SplitDomainsOfExpertise(list, u => u.Owner)) {
                                    target.NotifyElementUsage(sublist, metadata);
                                }

                                break;
                            }
                        case ClassKind.Publication: {
                                foreach (var publication in entry.Value) {
                                    target.NotifyPublication((Publication) publication, metadata);
                                }

                                break;
                            }
                        case ClassKind.EngineeringModel:
                            break;
                        case ClassKind.Iteration: {
                                // An iteration was edited
                                List<Iteration> list = entry.Value.ConvertAll(x => (Iteration)x);
                                target.NotifyIteration(list, metadata);

                                break;
                            }
                                //default:
                                //    target.NotifyOther (thing);
                                //    break;
                        }
                    }
                }

                // Update the revision so we don't get old models
                Logger.Trace ("Updating model revision {} -> {}", modelRevisions [engineeringModel.Iid], engineeringModel.RevisionNumber);
                modelRevisions [engineeringModel.Iid] = engineeringModel.RevisionNumber;
            } catch (Exception e) {
                Logger.Error (e, "EngineeringModel was not read successfulsly: {}", e);
                return;
            }
        }

        private static IEnumerable<List<TSource>> SplitDomainsOfExpertise<TSource, TKey>(List<TSource> initial, Func<TSource, TKey> keySelector) {
            if (configuration.Output.SplitOwners) {
                // Group using the provided keySelector function
                return initial.GroupBy (keySelector).Select(grp => grp.ToList()).ToList();
            } else {
                // An array containing just the given list itself
                return new List<TSource> [] { initial };
            }
        }

        public static void Main (string [] args)
        {
            Console.WriteLine ("Welcome to OCDT Notifier");

            var root = new ConfigurationBuilder ()
                .SetBasePath (System.IO.Directory.GetCurrentDirectory ())
                .AddYamlFile ("config.yml")
                .Build ();
            root.Bind ("ocdt_notifier", configuration);

            Logger.Debug ("Loaded configuration: {}", new YamlDotNet.Serialization.Serializer ().Serialize (configuration));

            new OCDTNotifier ();
        }


    }
}