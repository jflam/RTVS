﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Microsoft.R.Components {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Microsoft.R.Components.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Microsoft.R.Host.exe is missing. Click OK to open download link in the default browser..
        /// </summary>
        public static string Error_Microsoft_R_Host_Missing {
            get {
                return ResourceManager.GetString("Error_Microsoft_R_Host_Missing", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Command line text cannot be converted to default OS code page. Please set locale for non-Unicode programs in Control Panel -&gt; Region -&gt; Administrative to the locale you wish to use..
        /// </summary>
        public static string Error_ReplUnicodeCoversion {
            get {
                return ResourceManager.GetString("Error_ReplUnicodeCoversion", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Input is too long - no more than {0} characters expected..
        /// </summary>
        public static string InputIsTooLong {
            get {
                return ResourceManager.GetString("InputIsTooLong", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Starting R Session....
        /// </summary>
        public static string MicrosoftRHostStarting {
            get {
                return ResourceManager.GetString("MicrosoftRHostStarting", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to R Host process is stopped. Click Reset to start a new one..
        /// </summary>
        public static string MicrosoftRHostStopped {
            get {
                return ResourceManager.GetString("MicrosoftRHostStopped", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Stopping R Session....
        /// </summary>
        public static string MicrosoftRHostStopping {
            get {
                return ResourceManager.GetString("MicrosoftRHostStopping", resourceCulture);
            }
        }
    }
}