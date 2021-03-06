﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.R.Components.PackageManager.Model;

namespace Microsoft.R.Components.PackageManager {
    public interface IRPackageManager : IDisposable {
        event EventHandler LoadedPackagesInvalidated;
        event EventHandler InstalledPackagesInvalidated;
        event EventHandler AvailablePackagesInvalidated;

        IRPackageManagerVisualComponent VisualComponent { get; }

        IRPackageManagerVisualComponent GetOrCreateVisualComponent(IRPackageManagerVisualComponentContainerFactory visualComponentContainerFactory, int instanceId = 0);

        /// <summary>
        /// Get the list of packages installed in the library folders set for
        /// this session ie. in .libPaths().
        /// </summary>
        /// <returns>List of packages.</returns>
        /// <exception cref="RPackageManagerException">
        /// The package list couldn't be retrieved from the session.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        Task<IReadOnlyList<RPackage>> GetInstalledPackagesAsync();

        /// <summary>
        /// Get the list of packages that are available from the repositories
        /// set for this session ie. in getOption('repos').
        /// </summary>
        /// <returns>
        /// List of packages. Note that several fields will not be populated,
        /// you need to call <see cref="AddAdditionalPackageInfoAsync(RPackage)"/>
        /// for each package to get fill in the missing fields.
        /// </returns>
        /// <exception cref="RPackageManagerException">
        /// The package list couldn't be retrieved from the session.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        Task<IReadOnlyList<RPackage>> GetAvailablePackagesAsync();

        /// <summary>
        /// Install a package by sending install.packages() to the REPL.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <param name="libraryPath">
        ///     Optional library path (in any format). Pass null to use the default
        ///     for the session ie. the first one in .libPaths().
        /// </param>
        Task InstallPackageAsync(string name, string libraryPath);

        /// <summary>
        /// Uninstall a package by evaluating rtvs:::package_uninstall.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <param name="libraryPath">
        ///     Optional library path (in any format) where the package is installed.
        ///     Pass null to use the defaults for the session ie. in .libPaths().
        /// </param>
        /// <returns>
        /// <see cref="PackageLockState.Unlocked"/>  if the package was successfully 
        /// installed. <see cref="PackageLockState.LockedByRSession"/> or <see cref="PackageLockState.LockedByOther"/> 
        /// if the package was found to be installed and loaded in the REPL or 
        /// loaded by another process.
        /// </returns>
        Task<PackageLockState> UninstallPackageAsync(string name, string libraryPath);

        /// <summary>
        /// Uninstall and Install a package by evaluating rtvs:::package_update.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <param name="libraryPath">
        ///     Optional library path (in any format) where the package is installed.
        ///     Pass null to use the defaults for the session ie. in .libPaths().
        /// </param>
        /// <returns>
        /// <see cref="PackageLockState.Unlocked"/>  if the package was successfully 
        /// installed. <see cref="PackageLockState.LockedByRSession"/> or <see cref="PackageLockState.LockedByOther"/> 
        /// if the package was found to be installed and loaded in the REPL or 
        /// loaded by another process.
        /// </returns>
        Task<PackageLockState> UpdatePackageAsync(string name, string libraryPath);

        /// <summary>
        /// Load a package by sending library() to the REPL.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <param name="libraryPath">
        ///     Optional library path (in any format). Pass null to use the defaults
        ///     for the session ie. in .libPaths().
        /// </param>
        Task LoadPackageAsync(string name, string libraryPath);

        /// <summary>
        /// Unload a package by sending detach() to the REPL.
        /// </summary>
        /// <param name="name">Package name.</param>
        Task UnloadPackageAsync(string name);

        /// <summary>
        /// Package names that are currently loaded.
        /// </summary>
        /// <returns>Array of package names.</returns>
        /// <exception cref="RPackageManagerException">
        /// The package list couldn't be retrieved from the session.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        Task<string[]> GetLoadedPackagesAsync();

        /// <summary>
        /// Path of selected library folder, as returned .libPaths().
        /// </summary>
        /// <returns>Path in R format (ie. c:/libs/lib1).</returns>
        /// <exception cref="RPackageManagerException">
        /// The library list couldn't be retrieved from the session.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// </exception>
        Task<string> GetLibraryPathAsync();

        /// <summary>
        /// Check if the dll for the specified package is locked by the REPL
        /// session or external process.
        /// </summary>
        /// <param name="name">Package name.</param>
        /// <param name="libraryPath">Library path (in any format).</param>
        /// <returns>Lock state.</returns>
        Task<PackageLockState> GetPackageLockStateAsync(string name, string libraryPath);
    }
}