﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Common.Core;
using Microsoft.Common.Core.IO;
using Microsoft.Common.Core.OS;
using Microsoft.Win32;
using static System.FormattableString;

namespace Microsoft.R.Interpreters {
    /// <summary>
    /// Verifies that R is installed in the folder
    /// specified in settings. If nothing is specified
    /// settings try and find highest version.
    /// </summary>
    public sealed class RInstallation {
        private const string _rCoreRegKey = @"SOFTWARE\R-core\R";
        private const string _rServer = "R_SERVER";
        private static readonly string[] rFolders = new string[] { "MRO", "RRO", "R" };

        private readonly IRegistry _registry;
        private readonly IFileSystem _fileSystem;

        public RInstallation() :
            this(new RegistryImpl(), new FileSystem()) { }

        public RInstallation(IRegistry registry, IFileSystem fileSystem) {
            _registry = registry;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// Retrieves path to the latest (highest version) R installation
        /// from registry. Typically in the form 'Program Files\R\R-3.2.1'
        /// Selects highest from compatible versions, not just the highest.
        /// </summary>
        public IEnumerable<IRInterpreterInfo> GetCompatibleEngines(ISupportedRVersionRange svl = null) {
            var list = new List<IRInterpreterInfo>();

            // Get MRC from SQL
            var mrc = SqlRClientInstallation.GetMicrosoftRClientInfo(_registry, _fileSystem);
            if (mrc != null) {
                list.Add(mrc);
            }

            var engines = GetCompatibleEnginesFromRegistry(svl);
            engines = engines.Where(e => e.VerifyInstallation(svl, _fileSystem))
                             .OrderBy(e => e.Version);

            if (mrc == null) {
                // If MRC didn't come with SQL, try finding one in the R engines
                mrc = engines.FirstOrDefault(e => e.Name.Contains("Microsoft R"));
                if (mrc != null) {
                    list.Add(mrc);
                }
            }

            if (mrc != null) { 
                // Remove MRC and its duplicates
                engines = engines.Where(e => !e.InstallPath.PathEquals(mrc.InstallPath));
            }

            list.AddRange(engines);
            if (list.Count == 0) {
                var e = TryFindRInProgramFiles(svl);
                if (e != null) {
                    list.Add(e);
                }
            }

            return list;
        }

        /// <summary>
        /// Retrieves path to the all compatible R installations from registry. 
        /// </summary>
        private IEnumerable<IRInterpreterInfo> GetCompatibleEnginesFromRegistry(ISupportedRVersionRange svr) {
            svr = svr ?? new SupportedRVersionRange();
            var engines = GetInstalledEnginesFromRegistry().Where(e => svr.IsCompatibleVersion(e.Version));
            // Remove duplicates (MRC registers under multiple keys)
            var mrc = engines.FirstOrDefault(e => e.Name.Contains("Microsoft"));
            if(mrc != null) {
                var dupes = engines.Where(e => e.InstallPath.EqualsIgnoreCase(mrc.InstallPath)).Except(new IRInterpreterInfo[] { mrc });
                engines = engines.Except(dupes);
            }
            return engines;
        }

        /// <summary>
        /// Retrieves information on installed R versions in registry.
        /// </summary>
        private IEnumerable<IRInterpreterInfo> GetInstalledEnginesFromRegistry() {
            List<IRInterpreterInfo> engines = new List<IRInterpreterInfo>();

            // HKEY_LOCAL_MACHINE\SOFTWARE\R-core
            // HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\R-core
            // HKEY_LOCAL_MACHINE\SOFTWARE\R-core\R64\3.3.0 Pre-release
            using (IRegistryKey hklm = _registry.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)) {
                try {
                    using (var rKey = hklm.OpenSubKey(@"SOFTWARE\R-core\R")) {
                        foreach (var name in rKey.GetSubKeyNames()) {
                            using (var subKey = rKey.OpenSubKey(name)) {
                                var path = subKey.GetValue("InstallPath") as string;
                                if (!string.IsNullOrEmpty(path)) {
                                    // Convert '3.2.2.803 Microsoft R Client' to Microsoft R Client (version)
                                    // Convert '3.3.1' to 'R 3.3.1' for consistency
                                    engines.Add(new RInterpreterInfo(NameFromKey(name), path, _fileSystem));
                                }
                            }
                        }
                    }
                } catch (Exception) { }
            }
            return engines;
        }

        private static string NameFromKey(string key) {
            Version v;
            if (Version.TryParse(key, out v)) {
                return Invariant($"R {v}");
            } else {
                var index = key.IndexOfOrdinal("Microsoft R");
                if (index == 0) {
                    return key; // 'Microsoft R Open 'version'
                }
                if(index > 0) {
                    // 3.2.2.803 Microsoft R [Open | Client]
                    if(Version.TryParse(key.Substring(0, index).TrimEnd(), out v)) {
                        return Invariant($"{key.Substring(index).TrimEnd()} ({v})");
                    }
                }
            }
            return key; // fallback
        }

        private static Version GetRVersionFromFolderName(string folderName) {
            if (folderName.StartsWith("R-", StringComparison.OrdinalIgnoreCase)) {
                try {
                    Version v;
                    if (Version.TryParse(folderName.Substring(2), out v)) {
                        return v;
                    }
                } catch (Exception) { }
            }
            return new Version(0, 0);
        }

        private IRInterpreterInfo TryFindRInProgramFiles(ISupportedRVersionRange supportedVersions) {
            // Force 64-bit PF
            var programFiles = Environment.GetEnvironmentVariable("ProgramW6432");
            var baseRFolder = Path.Combine(programFiles, @"R");
            var versions = new List<Version>();
            try {
                if (_fileSystem.DirectoryExists(baseRFolder)) {
                    IEnumerable<IFileSystemInfo> directories = _fileSystem.GetDirectoryInfo(baseRFolder)
                                                                    .EnumerateFileSystemInfos()
                                                                    .Where(x => (x.Attributes & FileAttributes.Directory) != 0);
                    foreach (IFileSystemInfo fsi in directories) {
                        string subFolderName = fsi.FullName.Substring(baseRFolder.Length + 1);
                        Version v = GetRVersionFromFolderName(subFolderName);
                        if (supportedVersions.IsCompatibleVersion(v)) {
                            versions.Add(v);
                        }
                    }
                }
            } catch (IOException) {
                // Don't do anything if there is no RRO installed
            }

            if (versions.Count > 0) {
                versions.Sort();
                Version highest = versions[versions.Count - 1];
                var name = string.Format(CultureInfo.InvariantCulture, "R-{0}.{1}.{2}", highest.Major, highest.Minor, highest.Build);
                var path = Path.Combine(baseRFolder, name);
                var ri = new RInterpreterInfo(name, path);
                if (ri.VerifyInstallation(supportedVersions)) {
                    return ri;
                }
            }

            return null;
        }
    }
}
