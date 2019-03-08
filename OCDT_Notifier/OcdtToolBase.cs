// --------------------------------------------------------------------------------------------------------------------
// <copyright file="OcdtToolBase.cs" company="ESA">
//   Copyright (c) 2010-2015 European Space Agency.
//   All rights reserved. See COPYRIGHT.txt for details.
// </copyright>
// <summary>
//   Abstract base class for any OCDT command line tool (console application).
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace OCDT_Notifier
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NLog;

    using Ocdt.DataStoreServices;
    using Ocdt.DomainModel;
    using Ocdt.MessageServices;
    using RestSharp;

    /// <summary>
    /// Abstract base class for any OCDT command line tool (console application).
    /// </summary>
    public abstract class OcdtToolBase
    {
        /// <summary>
        /// Reference to the active logger
        /// </summary>
        protected static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Reference to the singleton instance of the <see cref="DomainObjectStore"/>.
        /// </summary>
        protected static readonly DomainObjectStore ObjStore = DomainObjectStore.Singleton;

        /// <summary>
        /// The data store message dispatcher.
        /// </summary>
        private static DataStoreMessageDispatcher dataStoreMessageDispatcher;

        /// <summary>
        /// Initialises a new instance of the <see cref="OcdtToolBase"/> class.
        /// </summary>
        protected OcdtToolBase()
        {
            this.WebServiceClient = new WebServiceClient();
        }

        /// <summary>
        /// Gets or sets the active <see cref="WebServiceClient"/>.
        /// </summary>
        protected WebServiceClient WebServiceClient { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="SiteDirectory"/> that is stored in the <see cref="DomainObjectStore"/>.
        /// </summary>
        protected SiteDirectory StoredSiteDirectory { get; set; }

        /// <summary>
        /// Open OCDT session.
        /// </summary>
        /// <param name="serverUriString">
        /// The server Uri String.
        /// </param>
        /// <param name="userName">
        /// The login of the user
        /// </param>
        /// <param name="password">
        /// The password of the user
        /// </param>
        /// <returns>
        /// Assertion that indicates success (true) or failure (false).
        /// </returns>
        protected bool OpenSession(string serverUriString, string userName, string password)
        {
            Uri serverUri;
            try
            {
                serverUri = new Uri(serverUriString);
            }
            catch (UriFormatException ex)
            {
                Logger.Fatal(ex + ": " + serverUriString);
                return false;
            }

            try
            {
                dataStoreMessageDispatcher = DataStoreMessageDispatcher.Singleton;
                dataStoreMessageDispatcher.DataStoreService = WebServiceClient;
                // Assign the permission service
                ObjStore.PermissionService = PermissionService.Singleton;

                this.WebServiceClient.Open(dataStoreUri: serverUri, userName: userName, password: password);
                Logger.Info($"Open session on URI {serverUri} for user \"{userName}\" was successful");

                this.StoredSiteDirectory = ObjStore.GetSiteDirectory();
                if (this.StoredSiteDirectory == null)
                {
                    Logger.Error("Site Directory was not loaded");
                    return false;
                }

                Logger.Info("Site Directory was successfully loaded");
            }
            catch (Exception e)
            {
                Logger.Fatal($"Open session on URI {serverUri} for user \"{userName}\" failed:\n{e}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get the <see cref="EngineeringModelSetup"/>(s) for given short name.
        /// </summary>
        /// <param name="engineeringModelShortName">
        /// The short of the <see cref="EngineeringModelSetup"/>(s) to find.
        /// If the short name is "*" all engineering model setups in the persistent data store will be returned.
        /// </param>
        /// <returns>
        /// The list of <see cref="EngineeringModelSetup"/>.
        /// </returns>
        protected List<EngineeringModelSetup> GetEngineeringModelSetups(string engineeringModelShortName)
        {
            var engineeringModelSetups = new List<EngineeringModelSetup>();

            if (engineeringModelShortName == "*")
            {
                engineeringModelSetups.AddRange(this.StoredSiteDirectory.Model.OrderBy(modelSetup => modelSetup.ShortName));
            }
            else
            {
                var foundEngineeringModelSetup = this.StoredSiteDirectory.Model.SingleOrDefault(ems => ems.ShortName == engineeringModelShortName);
                if (foundEngineeringModelSetup == null)
                {
                    Logger.Fatal("Cannot find engineering model with short name: \"{0}\"", engineeringModelShortName);
                    Environment.Exit(1);
                }
                else
                {
                    engineeringModelSetups.Add(foundEngineeringModelSetup);
                }
            }
            return engineeringModelSetups;
        }

        /// <summary>
        /// Open and load last iteration of given <see cref="EngineeringModelSetup"/>.
        /// </summary>
        /// <param name="engineeringModelSetup">
        /// The <see cref="EngineeringModelSetup"/> from which to load the last <see cref="Iteration"/>.
        /// </param>
        /// <returns>
        /// The last <see cref="Iteration"/> or null when not successful.
        /// </returns>
        protected Iteration OpenLastIterationOfEngineeringModel(EngineeringModelSetup engineeringModelSetup)
        {
            Utils.AssertNotNull(engineeringModelSetup);

            // Prepare a GET request to read the model
            var engineeringModelProxy = new EngineeringModel(iid: engineeringModelSetup.EngineeringModelIid);
            var lastIterationSetup = engineeringModelSetup.LastIterationSetup;
            var iterationProxy = new Iteration(iid: lastIterationSetup.IterationIid);
            engineeringModelProxy.Iteration.Add(iterationProxy);

            var queryParameters = new QueryParameters
            {
                Extent = ExtentQueryParameterKind.DEEP,
                IncludeAllContainers = true,
                IncludeReferenceData = true
            };

            Logger.Info($"Reading Engineering Model {engineeringModelSetup.ShortName}");
            Logger.Debug($"With query parameters {queryParameters}");

            try
            {
                this.WebServiceClient.Read(iterationProxy, queryParameters);
            }
            catch (Exception e)
            {
                Logger.Error("Engineering Model was not read successfully: {0}", e);
                return null;
            }

            var storedEngineeringModel = (EngineeringModel)ObjStore.GetByIid(engineeringModelProxy.Iid);
            if (storedEngineeringModel == null)
            {
                Logger.Fatal("Engineering Model was not loaded");
                return null;
            }

            Logger.Info(
                "Engineering Model {0} Iteration {1} \"{2}\" created on {3} was successfully loaded",
                storedEngineeringModel.EngineeringModelSetup.ShortName,
                lastIterationSetup.IterationNumber,
                lastIterationSetup.Description,
                lastIterationSetup.CreatedOn);

            engineeringModelSetup.SelectedIterationSetup = engineeringModelSetup.LastIterationSetup;

            var lastIteration = storedEngineeringModel.Iteration.Single(iteration => iteration.Iid == lastIterationSetup.IterationIid);
            return lastIteration;
        }
    }
}