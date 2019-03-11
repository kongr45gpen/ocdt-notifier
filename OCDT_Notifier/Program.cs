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
        protected static new readonly Logger Logger = LogManager.GetCurrentClassLogger ();

        /// <summary>
        /// The configuration, as specified by the user.
        /// </summary>
        public static Configuration configuration = new Configuration ();

        protected MattermostTarget target;

        /// <summary>
        /// The list of OCDT engineering models
        /// </summary>
        List<EngineeringModelSetup> engineeringModelSetups;

        /// <summary>
        /// List of the latest checked revisions for all OCDT things
        /// </summary>
        private Dictionary<Guid, int> allRevisions = new Dictionary<Guid, int> ();

        /// <summary>
        /// The list of the latest parsed revision for every Engineering Model
        /// </summary>
        private Dictionary<Guid, int> modelRevisions = new Dictionary<Guid, int> ();

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
                }
            }

            // Read the engineering model
            WebServiceClient.Read (engineeringModel);
            engineeringModel = (EngineeringModel)ObjStore.GetByIid (iid: engineeringModel.Iid);
            Logger.Trace ("Loaded EM {} with revision {} (setup revision {})", engineeringModelSetup.ShortName, engineeringModel.RevisionNumber, engineeringModelSetup.RevisionNumber);
            modelRevisions [engineeringModel.Iid] = engineeringModel.RevisionNumber;
        }

        private void poll (EngineeringModelSetup engineeringModelSetup)
        {
            Logger.Debug ("Querying EngineeringModelSetup {}", engineeringModelSetup.Name);
            var engineeringModel = (EngineeringModel)ObjStore.GetByIid (iid: engineeringModelSetup.EngineeringModelIid);
            var iteration = new Iteration (iid: engineeringModelSetup.LastIterationSetup.IterationIid);
            engineeringModel.Iteration.Add (iteration);

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

                    // For every thing...
                    foreach (var changedDomainObject in domainObjectStoreChange.ChangedDomainObjects) {
                        // Some convenience variables
                        ChangeKind changeKind = changedDomainObject.Value;
                        Thing thing = changedDomainObject.Key;

                        // Make sure that the thing has been updated
                        // TODO: Remove this check, as it is not necessary anymore
                        if (!allRevisions.ContainsKey (thing.Iid) || allRevisions [thing.Iid] != thing.RevisionNumber || changeKind == ChangeKind.Deleted || changeKind == ChangeKind.Conflicted) {
                            Logger.Warn ("New update for {}", thing);
                            allRevisions [thing.Iid] = thing.RevisionNumber;

                            // Add the Thing to the list, so it can be sent to the target
                            if (!updatedThings.ContainsKey(thing.ClassKind)) {
                                updatedThings [thing.ClassKind] = new List<Thing> ();
                            }
                            updatedThings [thing.ClassKind].Add (thing);

                            // Add the complementary data of the Thing
                            metadata [thing.Iid] = new Tuple<ChangeKind> (changeKind);
                        }
                    }

                    // Send the things to the target
                    foreach (KeyValuePair<ClassKind, List<Thing>> entry in updatedThings) {
                        Logger.Info ("Detected {} change", entry.Key);



                        switch (entry.Key) {
                        case ClassKind.ParameterValueSet:
                            // A parameter value was set
                            List<ParameterValueSet> list = entry.Value.ConvertAll (x => (ParameterValueSet)x);
                            // Group by domain of expertise, send a different message for each domain
                            foreach (var sublist in SplitDomainsOfExpertise(list, u=> u.Owner)) {

                                target.NotifyParameterValueSet (sublist, metadata);
                            }
                            break;
                        case ClassKind.EngineeringModel:
                        case ClassKind.Iteration:
                            break;
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
                var v1 = initial;
                var v2 = initial.GroupBy (keySelector);
                var v3 = v2.Select (grp => grp.ToList ());
                var v4 = v3.ToList ();
                return initial.GroupBy (keySelector).Select(grp => grp.ToList()).ToList();
            } else {
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