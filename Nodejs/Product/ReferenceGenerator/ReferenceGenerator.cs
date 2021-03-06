﻿//*********************************************************//
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2.0 License
//    
//    You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    Unless required by applicable law or agreed to in writing, software 
//    distributed under the License is distributed on an "AS IS" BASIS, 
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or 
//    implied. See the License for the specific language governing 
//    permissions and limitations under the License.
//
//*********************************************************//
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.NodejsTools;

namespace NodeReferenceGenerator {

    class NodeReferenceGenerator {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly StringBuilder _output = new StringBuilder();
        private readonly dynamic _all;

        public NodeReferenceGenerator() {
            _all = _serializer.DeserializeObject(File.ReadAllText("all.json"));
        }

        static void Main(string[] args) {
            var generator = new NodeReferenceGenerator();
            var js = generator.GenerateJavaScript();
            
            File.WriteAllText("all.js", File.ReadAllText("IntellisenseHeader.js") + js);

            var cs = generator.GenerateCSharp();
            File.WriteAllText("modules.cs", cs);
        }

        private string GenerateCSharp() {
            StringBuilder res = new StringBuilder();
            res.Append(@"//*********************************************************//
//    Copyright (c) Microsoft. All rights reserved.
//    
//    Apache 2.0 License
//    
//    You may obtain a copy of the License at
//    http://www.apache.org/licenses/LICENSE-2.0
//    
//    Unless required by applicable law or agreed to in writing, software 
//    distributed under the License is distributed on an ""AS IS"" BASIS, 
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or 
//    implied. See the License for the specific language governing 
//    permissions and limitations under the License.
//
//*********************************************************//

using System.Collections.Generic;
using Microsoft.VisualStudio.Language.Intellisense;

namespace Microsoft.NodejsTools.Intellisense {
    sealed partial class CompletionSource : ICompletionSource {
");
            res.AppendLine("        private static Dictionary<string, string> _nodejsModules = new Dictionary<string, string>() {");
            foreach (var module in _all["modules"]) {
                var desc = (string)module["desc"];
                StringBuilder output = new StringBuilder();
                var reader = XmlReader.Create(
                    new StringReader(
                        "<html>" + desc+ "</html>"
                    )
                );

                while (reader.Read()) {
                    switch (reader.NodeType) {
                        case XmlNodeType.Element:
                            switch (reader.Name) {
                                case "li": 
                                    output.Append("* ");
                                    break;
                            }                            
                            break;
                        case XmlNodeType.Text:
                            var text = reader.Value
                                .Replace("\\", "\\\\")
                                .Replace("\"", "\\\"")
                                .Replace("\r\n", "\\r\\n")
                                .Replace("\n", "\\r\\n");

                            output.Append(text);
                            break;
                        case XmlNodeType.EndElement:
                            switch (reader.Name) {
                                case "p":
                                case "li":
                                case "h1":
                                case "h2":
                                case "h3":
                                case "h4":
                                case "h5":
                                    output.Append("\\r\\n");
                                    break;
                            }
                            break;

                    }
                }

                // trim escaped newlines...
                while (output.ToString().EndsWith("\\r\\n")) {
                    output.Length -= 4;
                }

                res.AppendFormat("            {{\"{0}\", \"{1}\" }},",
                    FixModuleName(module["name"]),
                    output.ToString()
                );
                res.AppendLine();
            }
            res.AppendLine("        };");
            res.AppendLine(@"    }
}");
            return res.ToString();
        }

        private string GenerateJavaScript() {
            _output.AppendLine("global = {};");

            GenerateRequire(_output, _all);

            foreach (var misc in _all["miscs"]) {
                if (misc["name"] == "Global Objects") {
                    GenerateGlobals(misc["globals"]);
                    break;
                }
            }

            return _output.ToString();
        }

        private void GenerateGlobals(dynamic globals) {
            foreach (var global in globals) {
                if (global.ContainsKey("events") ||
                    global.ContainsKey("methods") ||
                    global.ContainsKey("properties") ||
                    global.ContainsKey("classes")) {


                    _output.AppendFormat("var {0} = new ", global["name"]);
                    GenerateModuleWorker(global, 0, "__" + global["name"]);
                    _output.Append(";");
                }
            }
        }

        private void GenerateModule(dynamic module, int indentation = 0) {
            string modName = FixModuleName(module["name"]);

            GenerateModuleWorker(module, indentation, modName);
        }


        private void GenerateModuleWorker(dynamic module, int indentation, string name) {
            _output.Append(' ', indentation * 4);
            _output.AppendFormat("function {0}() {{", name);
            _output.AppendLine();

            if (module.ContainsKey("desc")) {
                _output.Append(' ', (indentation + 1) * 4);
                _output.AppendFormat("/// <summary>{0}</summary>", FixDescription(module["desc"]));
                _output.AppendLine();
            }

            if (module.ContainsKey("methods")) {
                foreach (var method in module["methods"]) {
                    string body = null;
                    if (name == "path" && method["name"] is string) {
                        switch ((string)method["name"]) {
                            case "relative": body = ReferenceCode.PathRelativeBody; break;
                            case "normalize": body = ReferenceCode.PathNormalizeBody; break;
                            case "resolve": body = ReferenceCode.PathResolveBody; break;
                            case "join": body = ReferenceCode.PathJoinBody; break;
                        }
                    }
                    GenerateMethod(method, indentation + 1, body);
                }
            }

            if (module.ContainsKey("events")) {
                GenerateEvents(module["events"], indentation + 1);
            }

            if (module.ContainsKey("classes")) {
                foreach (var klass in module["classes"]) {
                    GenerateClass(klass, indentation + 1);
                }
            }

            if (module.ContainsKey("properties")) {
                Func<string, string> specializer;
                PropertySpecializations.TryGetValue(name, out specializer);
                GenerateProperties(module["properties"], indentation + 1, specializer);
            }

            _output.Append("}}");
        }

        private Dictionary<string, Func<string, string>> PropertySpecializations = MakePropertySpecializations();

        private static Dictionary<string, Func<string, string>> MakePropertySpecializations() {
            return new Dictionary<string, Func<string, string>>() {
                { "__process", ProcessPropertySpecialization }
            };
        }

        private static string ProcessPropertySpecialization(string propertyName) {
            switch (propertyName) {
                case "env":
                    return "{}";
                case "versions":
                    return "{node: '0.10.0', v8: '3.14.5.8'}";
                case "pid":
                    return "0";
                case "title":
                    return "''";
                case "platform":
                    return "'win32'";
                case "maxTickDepth":
                    return "1000";
                case "argv":
                    return "[ 'node.exe' ]";

            }
            return null;
        }

        private void GenerateClass(dynamic klass, int indentation) {
            string className = FixClassName(klass["name"]);
            _output.Append(' ', indentation * 4);
            _output.AppendFormat("function _{0}() {{", className);
            _output.AppendLine();

            if (klass.ContainsKey("methods")) {
                foreach (var method in klass["methods"]) {
                    GenerateMethod(method, indentation + 1);
                }
            }

            if (klass.ContainsKey("events")) {
                GenerateEvents(klass["events"], indentation + 1);
            }

            if (klass.ContainsKey("properties")) {
                GenerateProperties(klass["properties"], indentation + 1, null);
            }

            _output.Append(' ', indentation * 4);
            _output.AppendLine("}");

            _output.AppendLine();
            _output.AppendFormat("this.{0} = function() {{", className);
            _output.AppendLine();
            _output.AppendFormat("return new _{0}();", className);
            _output.AppendLine();
            _output.AppendLine("}");
        }

        private void GenerateProperties(dynamic properties, int indentation, Func<string, string> specializer) {
            foreach (var prop in properties) {
                string desc = String.Empty;
                
                if (prop.ContainsKey("desc")) {
                    desc = prop["desc"];

                    _output.Append(' ', indentation * 4);
                    _output.AppendFormat("/// <field name='{0}'>{1}</field>",
                        prop["name"],
                        FixDescription(desc));
                    _output.AppendLine();
                }

                string textRaw = String.Empty;
                if (prop.ContainsKey("textRaw")) {
                    textRaw = prop["textRaw"];
                }

                _output.Append(' ', indentation * 4);
                string value = null;
                if (desc.IndexOf("<code>Boolean</code>", StringComparison.Ordinal) != -1) {
                    value = "true";
                } else if (desc.IndexOf("<code>Number</code>", StringComparison.Ordinal) != -1) {
                    value = "0";
                } else if (desc.IndexOf("<code>Readable Stream</code>", StringComparison.Ordinal) != -1) {
                    value = "require('stream').Readable()";
                } else if (desc.IndexOf("<code>Writable Stream</code>", StringComparison.Ordinal) != -1 || textRaw == "process.stderr") {
                    value = "require('stream').Writable()";
                } else if (!String.IsNullOrWhiteSpace(textRaw)) {
                    int start, end;
                    if ((start = textRaw.IndexOf('{')) != -1 && (end = textRaw.IndexOf('}')) != -1 &&
                        start < end) {
                        string typeName = textRaw.Substring(start, end - start);
                        switch (typeName) {
                            case "Boolean":
                                value = "true";
                                break;
                            case "Number":
                                value = "0";
                                break;
                        }
                    }
                }

                if (value == null) {
                    if (specializer != null) {
                        value = specializer(prop["name"]) ?? "undefined";
                    }

                    if (value == null) {
                        value = "undefined";
                    }
                }
                _output.AppendFormat("this.{0} = {1};", prop["name"], value);
                _output.AppendLine();
            }
        }

        private void GenerateEvents(dynamic events, int indentation) {
            StringBuilder eventsDoc = new StringBuilder();
            eventsDoc.AppendLine();
            eventsDoc.Append(' ', indentation * 4);
            eventsDoc.AppendLine("/// <summary>");
            eventsDoc.Append(' ', indentation * 4);
            eventsDoc.AppendLine("/// Supported events: &#10;");
            StringBuilder eventNames = new StringBuilder();
            foreach (var ev in events) {
                if (ev["name"].IndexOf(' ') != -1) {
                    continue;
                }
                if (eventNames.Length != 0) {
                    eventNames.Append(", ");
                }
                eventNames.Append(ev["name"]);
                
                eventsDoc.Append(' ', indentation * 4);
                eventsDoc.Append("/// ");
                eventsDoc.Append(ev["name"]);                
                if (ev.ContainsKey("desc")) {
                    eventsDoc.Append(": ");
                    eventsDoc.Append(LimitDescription(ev["desc"]));
                }
                eventsDoc.Append("&#10;");
                eventsDoc.AppendLine();
            }
            eventsDoc.Append(' ', indentation * 4);
            eventsDoc.AppendLine("/// </summary>");

            string supportedEvents = String.Format("/// <summary>Supported Events: {0}</summary>", eventNames.ToString());
            _output.AppendLine("this.addListener = function(event, listener) {");
            _output.AppendLine(eventsDoc.ToString());
            _output.AppendLine("}");

            _output.AppendLine("this.once = function(event, listener) {");
            _output.AppendLine(eventsDoc.ToString());
            _output.AppendLine("}");

            _output.AppendLine("this.removeListener = function(event, listener) {");
            _output.AppendLine(supportedEvents);
            _output.AppendLine("}");
            
            _output.AppendLine("this.removeAllListeners = function(event) {");
            _output.AppendLine(supportedEvents);
            _output.AppendLine("}");

            _output.AppendLine("this.setMaxListeners = function(n) { }");
            
            _output.AppendLine("this.listeners = function(event) {");
            _output.AppendLine(supportedEvents);
            _output.AppendLine("}");

            _output.AppendLine("this.emit = function(event, arguments) {");
            _output.AppendLine(eventsDoc.ToString());
            _output.AppendLine("}");

            _output.AppendLine("this.on = function(event, listener) {");
            _output.AppendLine(eventsDoc.ToString());
            _output.AppendLine("}");
        }

        private void GenerateMethod(dynamic method, int indentation, string body = null) {
            _output.Append(' ', indentation * 4);
            _output.AppendFormat("this.{0} = function(", method["name"]);
            var signature = method["signatures"][0];
            bool first = true;
            foreach (var param in signature["params"]) {
                if (param["name"] == "...") {
                    break;
                }
                if (!first) {
                    _output.Append(", ");
                }

                _output.Append(FixModuleName(param["name"]));
                first = false;

                // TODO: Optional?
            }

            _output.AppendLine(") {");

            if (method.ContainsKey("desc")) {
                _output.Append(' ', (indentation + 1) * 4);
                _output.AppendFormat("/// <summary>{0}</summary>", FixDescription(method["desc"]));
                _output.AppendLine();
            }
            foreach (var curSig in method["signatures"]) {
                if (curSig["params"].Length > 0) {
                    _output.Append(' ', (indentation + 1) * 4);
                    _output.AppendLine("/// <signature>");

                    foreach (var param in curSig["params"]) {
                        _output.Append(' ', (indentation + 1) * 4);
                        _output.AppendFormat("/// <param name=\"{0}\"", param["name"]);

                        if (param.ContainsKey("type")) {
                            _output.AppendFormat(" type=\"{0}\"", FixModuleName(param["type"]));
                        }
                        _output.Append('>');

                        if (param.ContainsKey("desc")) {
                            _output.Append(param["desc"]);
                        }
                        _output.AppendLine("</param>");
                    }

                    if (curSig.ContainsKey("return")) {
                        object returnType = null, returnDesc = null;
                        if (curSig["return"].ContainsKey("type")) {
                            returnType = FixModuleName(curSig["return"]["type"]);
                        }

                        if (curSig["return"].ContainsKey("desc")) {
                            returnDesc = curSig["return"]["desc"];
                        }

                        if (returnType != null || returnDesc != null) {
                            _output.Append(' ', (indentation + 1) * 4);
                            _output.Append("/// <returns");

                            if (returnType != null) {
                                _output.AppendFormat(" type=\"{0}\">", returnType);
                            } else {
                                _output.Append('>');
                            }

                            if (returnDesc != null) {
                                _output.Append(returnDesc);
                            }
                            _output.AppendLine("</returns>");
                        }
                    }
                    _output.Append(' ', (indentation + 1) * 4);
                    _output.AppendLine("/// </signature>");
                }
            }

            if (body != null) {
                _output.AppendLine(body);
            } else if (method["name"].StartsWith("create") && method["name"].Length > 6) {
                _output.Append(' ', indentation * 4);
                _output.AppendFormat("return new this.{0}();", method["name"].Substring(6));
                _output.AppendLine();
            }
            _output.Append(' ', indentation * 4);
            _output.AppendLine("}");
        }

        private void GenerateRequire(StringBuilder res, dynamic all) {
            res.AppendLine("require = function () {");
            res.AppendLine("    var require_count = 0;");
            res.AppendLine("    var require_depth = 0;");
            res.AppendLine("    var max_require_depth = 5;");
            res.AppendLine("    var cache = {");

            foreach (var module in all["modules"]) {
                res.AppendFormat("        \"{0}\": null,", FixModuleName(module["name"]));
                res.AppendLine();
            }

            res.AppendLine("    }");
            res.AppendLine("    function make_module(module_name) {");
            res.AppendLine("        switch(module_name) { ");
            foreach (var module in all["modules"]) {
                res.AppendFormat("            case \"{0}\": return new ", FixModuleName(module["name"]));
                GenerateModule(module, 1);
                res.AppendLine(";");
            }
            res.AppendLine("        }");
            res.AppendLine("    }");

            res.Append(@"    var f = function(module) { 
        module = module.replace(/\\/g, '/');
        if(require_count++ >= 50) {
            require_count = 0;
            intellisense.progress();
        }
        var result = cache[module];
        if(typeof result !== 'undefined') {
            if(result === null) {
                // we lazily create only the modules which are actually used
                cache[module] = result = make_module(module);
            }
            return result;
        }
        // value not cached, see if we can look it up
        try { 
            var __prevFilename = __filename;
            var __prevDirname = __dirname;
            var __prevMaxRequireDepth = max_require_depth;
            require_depth++;
            try {
                // **NTVS** INSERT USER MODULE SWITCH HERE **NTVS**
            } finally {
                require_depth--;
            }
        } finally {
            __filename = __prevFilename;
            __dirname = __prevDirname;
            max_require_depth = __prevMaxRequireDepth;
        }
    }
    o.__proto__ = f.__proto__;
    f.__proto__ = o;
    return f;
}();");
        }


        private static dynamic FixModuleName(string module) {
            if (module == "tls_(ssl)") {
                return "tls";
            } else if (module == "Events") {
                return "events";
            }

            for (int i = 0; i < module.Length; i++) {
                if (!(Char.IsLetterOrDigit(module[i]) || module[i] == '_')) {
                    return module.Substring(0, i);
                }
            }
            return module;
        }

        private static string FixDescription(string desc) {
            return desc.Replace("\n", "&#10;");
        }

        private static string LimitDescription(string desc) {
            int newLine;
            if ((newLine = desc.IndexOf('\n')) != -1) {
                return desc.Substring(0, newLine).Replace("<p>", String.Empty).Replace("</p>", "") + " ...";
            }
            return desc.Replace("<p>", String.Empty).Replace("</p>", String.Empty);
        }

        private string FixClassName(string name) {
            name = name.Replace(" ", String.Empty);
            int dot;
            if ((dot = name.IndexOf('.')) != -1) {
                return name.Substring(dot + 1);
            }
            return name;
        }

    }
}
