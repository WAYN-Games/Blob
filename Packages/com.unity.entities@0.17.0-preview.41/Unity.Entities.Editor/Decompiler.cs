using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;

[assembly: InternalsVisibleTo("Unity.Entities.CodeGen.Tests")]
namespace Unity.Entities.Editor
{
    internal enum DecompiledLanguage
    {
        CSharpOnly,
        ILOnly,
        CSharpAndIL
    }

    internal static class Decompiler
    {
        public static Process DecompileIntoCSharp(string fullyQualifiedTypeName, string fullDllPath)
        {
            StringBuilder referencePaths = GetAllReferencePaths();

            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32Windows || Environment.OSVersion.Platform == PlatformID.Win32NT;
            string ilspycmdFullPath = Path.GetFullPath("Packages/com.unity.entities/Unity.Entities.Editor/PostprocessedILInspector/.ilspyfolder/ilspycmd.exe");

            if (isWindows)
            {
                ilspycmdFullPath = ilspycmdFullPath.Replace("/", "\\");
            }

            string ilSpyArgument = $"{(isWindows ? "" : ilspycmdFullPath)} \"{fullDllPath}\" -t \"{fullyQualifiedTypeName}\" {referencePaths}";

            Process outputCSharpProcess =
                new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        FileName = isWindows
                            ? ilspycmdFullPath
                            : $"{EditorApplication.applicationPath}/Contents/MonoBleedingEdge/bin/mono",
                        Arguments = ilSpyArgument,
                        RedirectStandardOutput = true
                    }
                };

            outputCSharpProcess.Start();
            return outputCSharpProcess;
        }

        private static StringBuilder GetAllReferencePaths()
        {
            var referencePaths = new StringBuilder();
            var processed = new HashSet<string>();

            foreach (Assembly assembly in
                AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)))
            {
                string path;

                try
                {
                    path = Path.GetDirectoryName(assembly.Location);
                }

                catch (ArgumentException)
                {
                    Debug.Log($"Unexpected path: {assembly.Location}");
                    continue;
                }

                if (processed.Contains(path))
                {
                    continue;
                }
                processed.Add(path);
                referencePaths.Append($"--referencepath \"{path}\" ");
            }

            return referencePaths;
        }

        public static (Process DecompileIntoCSharpProcess, Process DecompileIntoILProcess)
            StartDecompilationProcesses(TypeReference typeReference, DecompiledLanguage decompiledLanguage)
        {
            var assemblyDefinition = typeReference.Module.Assembly;

            var tempFolder = Path.GetTempPath();
            var fileName = $@"{tempFolder}TestAssembly.dll";
            var fileNamePdb = $@"{tempFolder}TestAssembly.pdb";
            var peStream = new FileStream(fileName, FileMode.Create);
            var symbolStream = new FileStream(fileNamePdb, FileMode.Create);

            assemblyDefinition.Write(
                peStream,
                new WriterParameters
                {
                    SymbolStream = symbolStream,
                    SymbolWriterProvider = new PortablePdbWriterProvider(),
                    WriteSymbols = true
                });

            peStream.Close();
            symbolStream.Close();

            StringBuilder referencePaths = GetAllReferencePaths();

            var isWin = Environment.OSVersion.Platform == PlatformID.Win32Windows || Environment.OSVersion.Platform == PlatformID.Win32NT;
            var ilspycmd = Path.GetFullPath("Packages/com.unity.entities/Unity.Entities.Editor/PostprocessedILInspector/.ilspyfolder/ilspycmd.exe");
            if (isWin)
                ilspycmd = ilspycmd.Replace("/", "\\");

            string ilSpyArgument = $"{(isWin ? "" : ilspycmd)} \"{fileName}\" -t \"{typeReference.FullName.Replace("/","+")}\" {referencePaths}";

            var outputCSharpProcess =
                decompiledLanguage == DecompiledLanguage.CSharpOnly || decompiledLanguage == DecompiledLanguage.CSharpAndIL
                ? new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = isWin
                        ? ilspycmd
                        : $"{EditorApplication.applicationPath}/Contents/MonoBleedingEdge/bin/mono",
                    Arguments = ilSpyArgument,
                    RedirectStandardOutput = true
                }
            }
            : null;

            var outputIlCodeProcess =
                decompiledLanguage == DecompiledLanguage.ILOnly || decompiledLanguage == DecompiledLanguage.CSharpAndIL
                ? new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = isWin
                        ? ilspycmd
                        : $"{EditorApplication.applicationPath}/Contents/MonoBleedingEdge/bin/mono",
                    Arguments = $"{ilSpyArgument} -il",
                    RedirectStandardOutput = true
                }
            }
            : null;

            outputCSharpProcess?.Start();
            outputIlCodeProcess?.Start();

            return (outputCSharpProcess, outputIlCodeProcess);
        }

        public static (string CSharpCode, string ILCode) DecompileIntoCSharpAndIL(TypeReference typeReference, DecompiledLanguage decompiledLanguage)
        {
            var(decompileIntoCSharpProcess, decompileIntoIlProcess) = StartDecompilationProcesses(typeReference, decompiledLanguage);
            return (decompileIntoCSharpProcess?.StandardOutput.ReadToEnd(), decompileIntoIlProcess?.StandardOutput.ReadToEnd());
        }
    }
}
