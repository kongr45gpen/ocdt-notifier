// --------------------------------------------------------------------------------------------------------------------
// <copyright file="GlobalAssembly.cs" company="ESA">
//   Copyright (c) 2010-2018 European Space Agency.
//   All rights reserved. See COPYRIGHT.txt for details.
// </copyright>
// <summary>
//   This file contains common AssemblyVersion data to be shared across all projects in this solution.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Reflection;

[assembly: AssemblyCompany("European Space Agency (ESA)")]
[assembly: AssemblyCopyright("Copyright © 2010-2018 European Space Agency. All rights reserved.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

// Version information for an assembly consists of the following four values:
// following the Semantic Versioning 2.0.0 recommendations, see http://semver.org/
//
//      Major Version
//      Minor Version
//      Patch Version
//      Build
//
// The Major and Minor components must be assigned manually.
// The Patch and Build components are automatically set by IncrementVersionNumber.targets during build.
//
[assembly: AssemblyVersion("3.0.61.*")]
