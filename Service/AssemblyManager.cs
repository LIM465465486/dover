﻿/*
 *  Dover Framework - OpenSource Development framework for SAP Business One
 *  Copyright (C) 2014  Eduardo Piva
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *  
 *  Contact me at <efpiva@gmail.com>
 * 
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dover.Framework.Attribute;
using Dover.Framework.DAO;
using Dover.Framework.Log;
using Dover.Framework.Model;
using Dover.Framework.Monad;
using Castle.Core.Logging;
using ICSharpCode.SharpZipLib.Zip;
using Dover.Framework.Factory;
using Dover.Framework.Interface;

namespace Dover.Framework.Service
{
    /// <summary>
    /// This is called from a temp AppDomain to load a Assembly and get it`s information.
    /// 
    /// It`s temp because after it`s call, the AppDomain will be Unload, unloading 
    /// all information loaded during this class call.
    /// </summary>
    internal class TempAssemblyLoader : MarshalByRefObject, ITempAssemblyLoader
    {
        public I18NService i18nService { get; set; }

        List<AssemblyInformation> ITempAssemblyLoader.GetAssemblyInfoFromBin(byte[] asmBytes, AssemblyInformation asmInfo)
        {
            Assembly asm = AppDomain.CurrentDomain.Load(asmBytes);
            asmInfo.Size = asmBytes.Length;
            SaveVersion(asm, asmInfo);
            SaveAddinAttribute(asm, asmInfo);
            List<AssemblyInformation> i18nDependencies = new List<AssemblyInformation>();
            List<AssemblyInformation> dependencies = new List<AssemblyInformation>();
            string[] defaultDependenciesNames = {"Castle.Core", "Castle.Facilities.Logging",
                                                "Castle.Services.Logging.Log4netIntegration",
                                                "Castle.Windsor", "ICSharpCode.SharpZipLib",
                                                "log4net", "SAPbouiCOM", "FrameworkInterface",
                                                "Interop.SAPbobsCOM"};
            HashSet<string> defaultDependenciesNamesSet = new HashSet<string>(defaultDependenciesNames);
            foreach (var dependency in defaultDependenciesNames)
            {
                Assembly dependencyAsm = AppDomain.CurrentDomain.Load(dependency);
                CheckAssembly(dependencies, dependencyAsm.GetName());
            }

            foreach (var dependency in asm.GetReferencedAssemblies())
            {
                if (!defaultDependenciesNamesSet.Contains(dependency.Name))
                    CheckAssembly(dependencies, dependency);
            }

            string[] i18nDirectories = Directory.GetDirectories(AppDomain.CurrentDomain.BaseDirectory);
            foreach (string i18nPath in i18nDirectories)
            {
                string i18n = Path.GetFileName(i18nPath);
                if (i18nService.IsValidi18NCode(i18n))
                {
                    CheckI18NAssembly(i18n, asm.GetName(), i18nDependencies);
                    foreach (var dependencyInfo in dependencies)
                    {
                        var dependency = AppDomain.CurrentDomain.Load(dependencyInfo.Name);
                        CheckI18NAssembly(i18n, dependency.GetName(), i18nDependencies);
                    }
                }
            }

            dependencies.AddRange(i18nDependencies);
            return dependencies;
        }

        private void CheckI18NAssembly(string i18n, AssemblyName assemblyName, List<AssemblyInformation> i18nDependencies)
        {
            string resourceAsm = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, i18n,
                                                assemblyName.Name + ".resources.dll");
            
            if (File.Exists(resourceAsm))
            {
                byte[] i18nResource = File.ReadAllBytes(resourceAsm);
                Assembly i18nResourceAssembly = AppDomain.CurrentDomain.Load(i18nResource);
                CheckI18NAssembly(i18n, i18nDependencies, i18nResourceAssembly.GetName());
            }
        }

        private void CheckI18NAssembly(string i18n, List<AssemblyInformation> i18nDependencies, AssemblyName assemblyName)
        {
            AssemblyInformation i18nInfo = GetAssemblyInformation(assemblyName);
            if (i18nInfo != null)
            {
                i18nInfo.FileName = Path.Combine(i18n, i18nInfo.FileName);
                i18nDependencies.Add(i18nInfo);
            }
        }

        private AssemblyInformation GetAssemblyInformation(AssemblyName assemblyName)
        {
            Assembly asm = AppDomain.CurrentDomain.Load(assemblyName);
            if (!string.IsNullOrEmpty(asm.Location) && File.Exists(asm.Location)
                && Path.GetDirectoryName(asm.Location).StartsWith(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)))
            {
                byte[] depBytes = File.ReadAllBytes(asm.Location);
                AssemblyInformation dependencyInformation = new AssemblyInformation()
                    {
                        Name = assemblyName.Name,
                        Description = assemblyName.Name,
                        Date = DateTime.Today,
                        Type = AssemblyType.Dependency,
                        FileName = assemblyName.Name + ".dll",
                        MD5 = FileUpdate.MD5Sum(depBytes)
                    };
                Assembly depAsm = AppDomain.CurrentDomain.Load(depBytes);
                SaveVersion(depAsm, dependencyInformation);
                dependencyInformation.Size = depBytes.Length;
                return dependencyInformation;
            }
            return null;
        }

        private void CheckAssembly(List<AssemblyInformation> dependencies, AssemblyName dependency)
        {
            AssemblyInformation dependencyInformation = GetAssemblyInformation(dependency);
            if (dependencyInformation != null)
            {
                dependencies.Add(dependencyInformation);
            }
        }

        private void SaveAddinAttribute(Assembly asm, AssemblyInformation asmInfo)
        {
            Type addInAttributeType = asm.GetType("Dover.Framework.Attribute.AddInAttribute");
            if (addInAttributeType == null)
                addInAttributeType = typeof(AddInAttribute);

            var types = (from type in asm.GetTypes()
                        where type.IsClass
                        select type);

            foreach (var type in types)
            {
                var attrs = type.GetCustomAttributes(addInAttributeType, true);
                if (attrs != null && attrs.Length > 0)
                {
                    var attr = attrs[0];
                    dynamic addInAttribute = attr;
                    if (!string.IsNullOrEmpty(addInAttribute.i18n))
                    {
                        asmInfo.Description = i18nService.GetLocalizedString(addInAttribute.i18n, asm);
                    }
                    else
                    {
                        asmInfo.Description = addInAttribute.Description;
                    }
                    asmInfo.Name = addInAttribute.Name;
                    asmInfo.Namespace = addInAttribute.Namespace;
                    break;
                }
            }
        }

        private void SaveVersion(Assembly asm, AssemblyInformation asmInfo)
        {
            var version = asm.GetName().Version;
            asmInfo.Major = version.Major;
            asmInfo.Minor = version.Minor;
            asmInfo.Build = version.Build;
            asmInfo.Revision = version.Revision;
        }
    }

    internal class AssemblyManager : MarshalByRefObject
    {
        private AssemblyDAO asmDAO;
        private LicenseManager licenseManager;
        private FileUpdate fileUpdate;
        private I18NService i18nService;
        public ILogger Logger { get; set; }


        public AssemblyManager(AssemblyDAO asmDAO, LicenseManager licenseManager, I18NService i18nService,
                                FileUpdate fileUpdate)
        {
            this.asmDAO = asmDAO;
            this.licenseManager = licenseManager;
            this.i18nService = i18nService;
            this.fileUpdate = fileUpdate;
        }

        internal void RemoveAddIn(string addinCode)
        {
            AssemblyInformation asm = asmDAO.GetAssemblyInformation(addinCode);
            if (asm != null)
            {
                List<AssemblyInformation> dependencies = asmDAO.GetDependencies(asm);
                foreach (var dep in dependencies)
                {
                    if (asmDAO.GetDependencyCount(dep) == 1)
                    {
                        asmDAO.RemoveAssembly(dep.Code);
                    }
                }
                asmDAO.RemoveAssembly(asm.Code);
                Logger.Info(string.Format(Messages.RemoveAddinSuccess, asm.Name));
            }
        }

        /// <summary>
        /// Return true if the addin is valid.
        /// </summary>
        /// <param name="path">Path name for the intended addin</param>
        /// <param name="datatable">DataTable serialized, to be displayed to the user with db change information.</param>
        /// <param name="addinName">Name of the addin, defined using an AddinAttribute</param>
        /// <param name="addinName">Namespace of the addin, defined using an AddinAttribute</param>
        /// <returns>true if addin is valid</returns>
        internal bool AddInIsValid(string path, out string datatable, out string addinName, out string addinNamespace)
        {
            string extension = Path.GetExtension(path);
            AppDomain testDomain = null;
            string mainDll = string.Empty;
            bool ret;
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);

            // check if it's a DLL or ZIP.
            if (extension != null && extension == ".dover")
            {
                mainDll = UnzipFile(path, tempDirectory);
            }
            else
            {
                throw new ArgumentException(Messages.InvalidAddInExtension);
            }
            if (mainDll == null)
            {
                datatable = string.Empty;
                addinName = string.Empty;
                addinNamespace = string.Empty;
                return false;
            }

            mainDll = Path.GetFileNameWithoutExtension(mainDll);
            testDomain = CreateTestDomain(mainDll, tempDirectory);
            try
            {
                testDomain.SetData("assemblyName", mainDll); // Used to get current AssemblyName for logging and reflection
                IApplication testApp = (IApplication)testDomain.CreateInstanceAndUnwrap("Framework", "Dover.Framework.Application");
                SAPServiceFactory.PrepareForInception(testDomain);
                var addinManager = testApp.Resolve<IAddinManager>();
                ret = addinManager.CheckAddinConfiguration(mainDll, out datatable, out addinName, out addinNamespace);
            }
            finally
            {
                AppDomain.Unload(testDomain);
                try
                {
                    Directory.Delete(tempDirectory, true);
                } catch (Exception e)
                {
                    Logger.Debug(string.Format("Directory {0} not cleaned", tempDirectory), e);
                }
            }
            return ret;
        }

        /// <summary>
        /// Unzip a zip file in a specific path.
        /// </summary>
        /// <param name="path">path for the zip file</param>
        /// <param name="destinationFolder">destination folder to unzip</param>
        /// <returns>main DLL found on zip archive.</returns>
        private string UnzipFile(string path, string destinationFolder)
        {
            using (ZipInputStream s = new ZipInputStream(File.OpenRead(path)))
            {

                ZipEntry theEntry;
                while ((theEntry = s.GetNextEntry()) != null)
                {

                    Console.WriteLine(theEntry.Name);

                    string baseDirName = Path.GetDirectoryName(theEntry.Name);
                    string directoryName = Path.Combine(destinationFolder,  baseDirName);
                    string fileName = Path.GetFileName(theEntry.Name);

                    // create directory
                    if (baseDirName.Length > 0)
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    if (fileName != String.Empty)
                    {
                        using (FileStream streamWriter = File.Create(Path.Combine(destinationFolder, theEntry.Name)))
                        {

                            int size = 2048;
                            byte[] data = new byte[2048];
                            while (true)
                            {
                                size = s.Read(data, 0, data.Length);
                                if (size > 0)
                                {
                                    streamWriter.Write(data, 0, size);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            string mainDll = Path.GetFileNameWithoutExtension(path) + ".dll";
            if (!File.Exists(Path.Combine(destinationFolder, mainDll)))
            {
                throw new ArgumentException(Messages.InvalidAddInExtension);
            }

            return mainDll;
        }

        private AppDomain CreateTestDomain(string mainDll, string tempDirectory)
        {
            AppDomainSetup setup = new AppDomainSetup();
            setup.ApplicationName = "Addin.test";
            setup.ApplicationBase = tempDirectory;

            AppDomain testDomain = AppDomain.CreateDomain("Addin.test", null, setup);
            return testDomain;
        }

        /// <summary>
        /// Register a valid addin. WARNING: this method does not check if the addin is valid, it just
        /// save it to the database with the correct data structure, such as version and MD5SUM. It's important
        /// to call AddInIsValid if you're not sure on what is being passed as path, otherwise there will
        /// be errors during addin startup.
        /// </summary>
        /// <param name="path">path for the file to be saved</param>
        /// <param name="addinName">Name of the addin</param>
        /// <param name="addinNamespace">Namespace of the addin</param>
        internal void SaveAddIn(string path, string addinName, string addinNamespace)
        {
            if (path == null || path.Length < 4)
            {
                Logger.Error(string.Format(Messages.SaveAddInError, path.Return( x => x, String.Empty)));
            }
            else
            {
                try
                {
                    string directory;
                    string fileName = Path.GetFileName(path);
                    if (!fileName.EndsWith(".dover"))
                    {
                        Logger.Error(Messages.InvalidAddInExtension);
                    }

                    AssemblyType type = (addinName == "Framework" && addinNamespace == "Dover") ? AssemblyType.Core : AssemblyType.Addin;

                    directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                    Directory.CreateDirectory(directory);
                    fileName = UnzipFile(path, directory);

                    string addinCode = asmDAO.GetAssemblyCode(addinName, addinNamespace, AssemblyType.Addin);
                    AssemblyInformation existingAsm = null;
                    if (addinCode != null)
                        existingAsm = asmDAO.GetAssemblyInformation(addinCode);
                    AssemblyInformation newAsm = GetCurrentAsm(directory, fileName, type);
                    AssemblyInformation savedAsm = SaveIfNotExistsOrDifferent(existingAsm, newAsm, directory);

                    Logger.Info(string.Format(Messages.SaveAddInSuccess, path));
                }
                catch (InvalidCastException e)
                {
                    Logger.Error(string.Format(Messages.UpdateFrameworkError));
                }
                catch (Exception e)
                {
                    Logger.Error(string.Format(Messages.SaveAddInError, path), e);
                }
            }

        }

        internal void UpdateFrameworkAssemblies(string appFolder)
        {
            Logger.Debug(DebugString.Format(Messages.UpdatingAssembly, AssemblyType.Core));

            AssemblyInformation asm = asmDAO.GetCoreAssemblyInformation();
            UpdateFrameworkDBAssembly(ref asm);
            fileUpdate.UpdateAppDataFolder(asm, appFolder);
        }

        private void UpdateFrameworkDBAssembly(ref AssemblyInformation asm)
        {
            const string defaultFrameworkDll = "Framework.dll";

            if (asm == null)
            {
                AssemblyInformation newAsm = GetCurrentAsm(AppDomain.CurrentDomain.BaseDirectory, defaultFrameworkDll, AssemblyType.Core);
                AssemblyInformation savedAsm = SaveIfNotExistsOrDifferent(null, newAsm, AppDomain.CurrentDomain.BaseDirectory);
                asm = savedAsm;
            }
            else
            {
                if (asmDAO.AutoUpdateEnabled(asm))
                {
                    asm = UpdateModuleDBAssembly(asm, AssemblyType.Core);
                }
            }
        }

        internal void UpdateAddinsDBAssembly()
        {
            List<AssemblyInformation> autoUpdateAddins = asmDAO.GetAutoUpdateAssemblies();
            foreach (var asm in autoUpdateAddins)
            {
                UpdateModuleDBAssembly(asm, AssemblyType.Addin);
            }
        }

        private AssemblyInformation UpdateModuleDBAssembly(AssemblyInformation asm, AssemblyType assemblyType)
        {
            try
            {
                AssemblyInformation newAsm = GetCurrentAsm(AppDomain.CurrentDomain.BaseDirectory, asm.FileName, assemblyType);
                AssemblyInformation savedAsm = SaveIfNotExistsOrDifferent(asm, newAsm, AppDomain.CurrentDomain.BaseDirectory);
                Logger.Info(string.Format(Messages.FileUpdated, savedAsm.Name, savedAsm.Version));
                return savedAsm;
            }
            catch (FileNotFoundException)
            {
                // Ignore it, use DB version.
                Logger.Warn(string.Format(Messages.FileMissing, asm.Name, "?"));
                return asm;
            }
        }

        private AssemblyInformation GetCurrentAsm(string directory, string filename, AssemblyType type)
        {
            string path = Path.Combine(directory, filename);
            byte[] asmBytes;

            AssemblyInformation newAsm = new AssemblyInformation();
            newAsm.FileName = filename;
            asmBytes = File.ReadAllBytes(path);

            GetAssemblyInfoFromBin(directory, asmBytes, newAsm);
            // calculate MD5 based on all binary, so if something changes trigger a full directory update.
            newAsm.MD5 = FileUpdate.MD5Sum(asmBytes); 
            newAsm.Size = asmBytes.Length;
            newAsm.Date = DateTime.Now;
            newAsm.Type = type;

            return newAsm;
        }

        private AssemblyInformation SaveIfNotExistsOrDifferent(AssemblyInformation existingAsm,
            AssemblyInformation newAsm, string baseDirectory)
        {
            if (existingAsm != null)
                newAsm.Code = existingAsm.Code; // Prepare for update.

            if (existingAsm == null || newAsm.CompareTo(existingAsm) == 1
                || (newAsm.Version == existingAsm.Version && newAsm.MD5 != existingAsm.MD5))
            {
                byte[] asmBytes = File.ReadAllBytes(Path.Combine(baseDirectory, newAsm.FileName));
                asmDAO.SaveAssembly(newAsm, asmBytes);

                // TODO: remove deleteDependencies from SaveAssembly do some sort of incremental SaveIfNotExists
                // Updating each assembly deparatly. Right now we just update everything if mainDll differs.
                foreach (var dependency in newAsm.Dependencies)
                {
                    string dependencyCode = asmDAO.GetDependencyCode(dependency.MD5);
                    if (string.IsNullOrEmpty(dependencyCode))
                    {
                        asmBytes = File.ReadAllBytes(Path.Combine(baseDirectory, dependency.FileName));
                        asmDAO.SaveAssemblyDependency(newAsm, dependency, asmBytes);
                    }
                    else
                    {
                        asmDAO.SaveAssemblyDependency(newAsm, dependencyCode);
                    }
                }
                asmDAO.DeleteOrphanDependency();
                licenseManager.UpdateAddinDueDate(newAsm);
                return newAsm;
            }
            else
            {
                return existingAsm;
            }
        }

        private void GetAssemblyInfoFromBin(string directory, byte[] asmBytes, AssemblyInformation asmInfo)
        {
            AppDomain tempDomain;
            var setup = new AppDomainSetup();
            setup.ApplicationName = "Dover.GetAssemblyInformation";
            setup.ApplicationBase = directory;
            tempDomain = AppDomain.CreateDomain("Dover.GetAssemblyInformation", null, setup);
            List<AssemblyInformation> dependencyInformation;

            try
            {
                IApplication app = (IApplication)tempDomain.CreateInstanceAndUnwrap("Framework",
                    "Dover.Framework.Application");
                SAPServiceFactory.PrepareForInception(tempDomain);
                ITempAssemblyLoader asmLoader = app.Resolve<ITempAssemblyLoader>();
                dependencyInformation = asmLoader.GetAssemblyInfoFromBin(asmBytes, asmInfo);
                asmInfo.Dependencies = new List<AssemblyInformation>();

                // The appDomain will die, clone return.
                foreach (var dep in dependencyInformation)
                {
                    AssemblyInformation newInfo = new AssemblyInformation();
                    newInfo.Build = dep.Build;
                    newInfo.Code = dep.Code;
                    newInfo.Date = dep.Date;
                    newInfo.FileName = string.Copy(dep.FileName);
                    newInfo.Major = dep.Major;
                    newInfo.MD5 = string.Copy(dep.MD5);
                    newInfo.Minor = dep.Minor;
                    newInfo.Name = string.Copy(dep.Name);
                    newInfo.Namespace = string.Empty;
                    newInfo.Description = string.Copy(dep.Description);
                    newInfo.Revision = dep.Revision;
                    newInfo.Size = dep.Size;
                    newInfo.Type = dep.Type;
                    asmInfo.Dependencies.Add(newInfo);
                }

            }
            finally
            {
                AppDomain.Unload(tempDomain);
            }
        }
    }
}
