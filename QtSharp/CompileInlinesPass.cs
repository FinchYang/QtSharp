﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using CppSharp.AST;
using CppSharp.Parser;
using CppSharp.Passes;

namespace QtSharp
{
    public class CompileInlinesPass : TranslationUnitPass
    {
        private readonly string qmake;
        private readonly string make;

        public CompileInlinesPass(string qmake, string make)
        {
            this.qmake = qmake;
            this.make = make;
        }

        public override bool VisitLibrary(ASTContext library)
        {
            string error;
            const string qtVersionVariable = "QT_VERSION";
            var qtVersion = ProcessHelper.Run(this.qmake, string.Format("-query {0}", qtVersionVariable), out error);
            var qtVersionFile = Path.Combine(this.Driver.Options.OutputDir, qtVersionVariable);
            var dir = Path.Combine(this.Driver.Options.OutputDir, "release");
            var inlines = Path.GetFileName(string.Format("lib{0}.a", this.Driver.Options.InlinesLibraryName));
            var libFile = Path.Combine(dir, inlines);
            var qtVersionFileInfo = new FileInfo(qtVersionFile);
            var inlinesFileInfo = new FileInfo(libFile);
            string text = string.Empty;
            if (!qtVersionFileInfo.Exists || (text = File.ReadAllText(qtVersionFile)) != qtVersion ||
                !inlinesFileInfo.Exists || qtVersionFileInfo.CreationTimeUtc > inlinesFileInfo.CreationTimeUtc ||
                qtVersionFileInfo.LastWriteTimeUtc > inlinesFileInfo.LastWriteTimeUtc)
            {
                if (text != qtVersion)
                {
                    File.WriteAllText(qtVersionFile, qtVersion);
                }
                if (!this.CompileInlines())
                {
                    return false;
                }
            }
            var parserOptions = new ParserOptions();
            parserOptions.addLibraryDirs(dir);
            parserOptions.FileName = inlines;
            using (var parserResult = ClangParser.ParseLibrary(parserOptions))
            {
                if (parserResult.Kind == ParserResultKind.Success)
                {
                    var nativeLibrary = CppSharp.ClangParser.ConvertLibrary(parserResult.Library);
                    this.Driver.Symbols.Libraries.Add(nativeLibrary);
                    this.Driver.Symbols.IndexSymbols();
                    parserResult.Library.Dispose();
                }
            }
            return true;
        }

        private bool CompileInlines()
        {
            var pro = string.Format("{0}.pro", this.Driver.Options.InlinesLibraryName);
            var path = Path.Combine(this.Driver.Options.OutputDir, pro);
            var proBuilder = new StringBuilder();
            proBuilder.AppendFormat("QT += {0}\n",
                                    string.Join(" ",
                                                this.Driver.Options.Headers.Select(h => h.Substring("Qt".Length).ToLowerInvariant())));
            proBuilder.Append("CONFIG += c++11\n");
            proBuilder.Append("QMAKE_CXXFLAGS += -fkeep-inline-functions\n");
            proBuilder.AppendFormat("TARGET = {0}\n", this.Driver.Options.InlinesLibraryName);
            proBuilder.Append("TEMPLATE = lib\n");
            proBuilder.AppendFormat("SOURCES += {0}\n", Path.ChangeExtension(pro, "cpp"));
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                proBuilder.Append("LIBS += -loleaut32 -lole32");
            }
            File.WriteAllText(path, proBuilder.ToString());
            string error;
            ProcessHelper.Run(this.qmake, string.Format("\"{0}\"", path), out error);
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine(error);
                return false;
            }
            var makefile = File.Exists(Path.Combine(Driver.Options.OutputDir, "Makefile.Release")) ? "Makefile.Release" : "Makefile";
            ProcessHelper.Run(this.make, string.Format("-j{0} -f {1}", Environment.ProcessorCount + 1, makefile), out error, true);
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine(error);
                return false;
            }
            return true;
        }
    }
}
