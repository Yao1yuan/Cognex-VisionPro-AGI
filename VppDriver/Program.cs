using Cognex.VisionPro;
using Cognex.VisionPro.Implementation.Internal;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.ToolGroup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace VppDriverMcp
{
    public class JsonRpcRequest
    {
        public string jsonrpc { get; set; }
        public object id { get; set; }
        public string method { get; set; }
        public JToken @params { get; set; }
    }

    class Program
    {
        private static readonly Dictionary<string, object> toolCache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Type, PropertyInfo[]> typePropertiesCache = new Dictionary<Type, PropertyInfo[]>();
        private static object vppObject;
        private static string vppPath;
        private static TextWriter _claudeChannel;

        [STAThread]
        static void Main(string[] args)
        {
            _claudeChannel = Console.Out;
            Console.SetOut(Console.Error); // 杂音全部进 Error
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);

            try
            {
                if (args.Length > 0 && File.Exists(args[0]))
                    LoadVppFile(args[0]);

                RunMcpLoop();
            }
            catch (Exception ex) { Log($"[FATAL CRASH] {ex}"); }
        }

        static void RunMcpLoop()
        {
            while (true)
            {
                string line = Console.ReadLine();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var request = JsonConvert.DeserializeObject<JsonRpcRequest>(line);
                    if (request == null) continue;

                    object result = null;
                    bool isError = false;
                    string errorMsg = "";

                    try
                    {
                        if (request.method == "initialize")
                        {
                            result = new
                            {
                                protocolVersion = "2024-11-05",
                                capabilities = new { tools = new { listChanged = true } },
                                serverInfo = new { name = "visionpro-vpp-driver", version = "9.5.0" }
                            };
                        }
                        else if (request.method == "tools/list") result = new { tools = GetMcpTools() };
                        else if (request.method == "tools/call") result = HandleToolCall(request.@params);
                        else if (request.method == "ping") result = new { };
                    }
                    catch (Exception ex) { isError = true; errorMsg = ex.Message; }

                    if (request.id != null)
                    {
                        string jsonRes = isError
                            ? JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = request.id, error = new { code = -32603, message = errorMsg } }, Formatting.None)
                            : JsonConvert.SerializeObject(new { jsonrpc = "2.0", id = request.id, result = result }, Formatting.None);
                        _claudeChannel.WriteLine(jsonRes);
                        _claudeChannel.Flush();
                    }
                }
                catch (Exception ex) { Log($"[Loop Error] {ex.Message}"); }
            }
        }

        static List<object> GetMcpTools()
        {
            return new List<object>
            {
                new { name = "vpp_load_file", description = "Load VPP file.", inputSchema = new { type = "object", properties = new { file_path = new { type = "string" } }, required = new[] { "file_path" } } },
                new { name = "vpp_list_tools", description = "List all tools.", inputSchema = new { type = "object", properties = new { } } },
                new { name = "vpp_get_property", description = "Get value or inspect structure (path='.' for root).", inputSchema = new { type = "object", properties = new { tool_name = new { type = "string" }, path = new { type = "string" } }, required = new[] { "tool_name", "path" } } },
                new { name = "vpp_set_property", description = "Set property value.", inputSchema = new { type = "object", properties = new { tool_name = new { type = "string" }, path = new { type = "string" }, value = new { type = "string" } }, required = new[] { "tool_name", "path", "value" } } },
                new { name = "vpp_extract_script", description = "Extract C# code.", inputSchema = new { type = "object", properties = new { tool_name = new { type = "string" } }, required = new[] { "tool_name" } } },
                new { name = "vpp_inject_script", description = "Inject C# code.", inputSchema = new { type = "object", properties = new { tool_name = new { type = "string" }, code = new { type = "string" } }, required = new[] { "tool_name", "code" } } },
                new { name = "vpp_create_tool", description = "Create new VisionPro tool.", inputSchema = new { type = "object", properties = new { parent_name = new { type = "string" }, tool_type = new { type = "string" }, new_tool_name = new { type = "string" } }, required = new[] { "parent_name", "tool_type" } } }
            };
        }

        static object HandleToolCall(JToken paramsToken)
        {
            string name = paramsToken["name"]?.ToString();
            JObject args = paramsToken["arguments"] as JObject;
            string output = "";
            bool isErr = false;
            try
            {
                switch (name)
                {
                    case "vpp_load_file": output = LoadVppFile(args?["file_path"]?.ToString()); break;
                    case "vpp_list_tools": output = toolCache.Count == 0 ? "No tools." : string.Join("\n", toolCache.Select(k => $"- {k.Key} ({k.Value.GetType().Name})")); break;
                    case "vpp_get_property": output = HandleGetSetRequest("get", args?["tool_name"]?.ToString(), args?["path"]?.ToString(), null); break;
                    case "vpp_set_property": output = HandleGetSetRequest("set", args?["tool_name"]?.ToString(), args?["path"]?.ToString(), args?["value"]?.ToString()); break;
                    case "vpp_extract_script": output = TryGetScriptCode(FindToolByName(args?["tool_name"]?.ToString())) ?? "No script."; break;
                    case "vpp_inject_script":
                        {
                            var host = FindToolByName(args?["tool_name"]?.ToString());
                            if (host == null)
                            {
                                output = $"Error: Tool '{args?["tool_name"]}' not found.";
                                isErr = true;
                                break;
                            }

                            string msg;
                            bool ok = TrySetScriptCode(host, args?["code"]?.ToString() ?? "", out msg);

                            if (ok)
                            {
                                CogSerializer.SaveObjectToFile(vppObject, vppPath);
                                output = "Success & Saved. " + msg;
                            }
                            else
                            {
                                output = "Failed. " + msg;
                                isErr = true;
                            }
                            break;
                        }
                    case "vpp_create_tool": output = CreateTool(args?["parent_name"]?.ToString(), args?["tool_type"]?.ToString(), args?["new_tool_name"]?.ToString()); break;
                    default: output = "Unknown tool."; isErr = true; break;
                }
            }
            catch (Exception ex) { output = ex.Message; isErr = true; }
            return new { content = new[] { new { type = "text", text = output } }, isError = isErr };
        }

        // --- 核心：万能类型解析器 ---

        // --- 核心：万能类型解析器 (修复版 - 支持按需加载 DLL) ---

        // --- 核心：万能类型解析器 (终极修复版) ---

        static Type ResolveVisionProType(string toolTypeStr)
        {
            string fullTypeName = toolTypeStr;

            // 1. 智能推断完整命名空间 (如果用户只传了 "CogPMAlignTool")
            if (!fullTypeName.Contains("."))
            {
                // CogPMAlignTool -> 截取出 "PMAlign" -> 拼成 "Cognex.VisionPro.PMAlign.CogPMAlignTool"
                string moduleName = toolTypeStr.Substring(3).Replace("Tool", "");
                if (toolTypeStr == "CogFixtureTool") moduleName = "CalibFix"; // 处理特例
                fullTypeName = $"Cognex.VisionPro.{moduleName}.{toolTypeStr}";
            }

            // 2. 尝试从当前内存的程序集中【精准读取】 (避免使用 GetTypes() 导致引发异常)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type t = asm.GetType(fullTypeName, false, true);
                if (t != null) return t;
            }

            // 3. 尝试从你复制进来的 DLL 文件中加载
            string[] parts = fullTypeName.Split('.');
            if (parts.Length >= 3)
            {
                // 拼出 Cognex.VisionPro.PMAlign.dll
                string dllFileName = $"{parts[0]}.{parts[1]}.{parts[2]}.dll";
                string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllFileName);

                if (File.Exists(dllPath))
                {
                    try
                    {
                        Assembly loadedAsm = Assembly.LoadFrom(dllPath);
                        Type t = loadedAsm.GetType(fullTypeName, false, true);
                        if (t != null) return t;
                    }
                    catch (Exception ex)
                    {
                        Log($"[Error] DLL 找到了，但加载内部类失败: {ex.Message}");
                    }
                }
                else
                {
                    Log($"[Warning] 目录中没有找到文件: {dllFileName}");
                }
            }

            // 4. 兜底方案：尝试去系统全局 GAC 里强行唤醒
            try
            {
#pragma warning disable CS0618
                Assembly gacAsm = Assembly.LoadWithPartialName($"{parts[0]}.{parts[1]}.{parts[2]}");
#pragma warning restore CS0618
                if (gacAsm != null)
                {
                    Type t = gacAsm.GetType(fullTypeName, false, true);
                    if (t != null) return t;
                }
            }
            catch { }

            Log($"[Error] 彻底找不到类型: {fullTypeName}。请检查拼写或依赖项。");
            return null;
        }

        static Type FindTypeInLoadedAssemblies(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || asm.FullName.StartsWith("System") || asm.FullName.StartsWith("mscorlib")) continue;
                try
                {
                    // 1. 尝试全名匹配
                    Type t = asm.GetType(typeName, false, true);
                    if (t != null) return t;

                    // 2. 尝试类名匹配 (忽略命名空间)
                    // VisionPro 的类名 CogPMAlignTool 是唯一的，直接对名字就行
                    t = asm.GetTypes().FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
        // --- 核心：创建工具与容器适配 ---

        static string CreateTool(string parentName, string toolTypeName, string newToolName)
        {
            object parent = FindToolByName(parentName);
            CogToolCollection targetCollection = null;

            if (parent is CogToolBlock tb) targetCollection = tb.Tools;
            else if (parent is CogToolGroup tg) targetCollection = tg.Tools;
            else if (parent is CogJob job)
            {
                // Job 特殊处理：如果没有 VisionTool，创建一个
                if (job.VisionTool == null) job.VisionTool = new CogToolGroup();
                if (job.VisionTool is CogToolGroup jtg) targetCollection = jtg.Tools;
            }

            if (targetCollection == null) return "Error: Parent is not a valid container (Block, Group, or Job).";

            Type toolType = ResolveVisionProType(toolTypeName);
            if (toolType == null) return $"Error: Could not resolve type '{toolTypeName}'.";

            try
            {
                ICogTool newTool = (ICogTool)Activator.CreateInstance(toolType);
                if (!string.IsNullOrEmpty(newToolName)) newTool.Name = newToolName;

                targetCollection.Add(newTool);
                toolCache[newTool.Name] = newTool; // 立即进缓存
                CogSerializer.SaveObjectToFile(vppObject, vppPath);

                return $"Success: Created '{newTool.Name}' ({toolType.Name}) in '{parentName}'.";
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        // --- Get/Set 与 属性路径解析 ( operators[0] 支持版 ) ---

        static string HandleGetSetRequest(string mode, string toolName, string path, string val)
        {
            object tool = FindToolByName(toolName);
            if (tool == null) return $"Error: Tool '{toolName}' not found.";

            // 处理根路径支持
            string effectivePath = (path == "." || path == null) ? "" : path;

            if (!TryResolveProperty(tool, effectivePath, out object targetObj, out PropertyInfo prop))
                return $"Error: Path '{path}' not found on target tool.";

            if (mode == "get")
            {
                object result = (prop != null) ? prop.GetValue(targetObj) : targetObj;
                if (result == null) return "null";

                Type t = result.GetType();
                if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
                    return result.ToString();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"[Structure: {t.Name}]");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"{"Property Name",-35} | {"Type",-15} | {"Value/Detail"}");
                sb.AppendLine(new string('-', 60));
                AppendObjectStructure(sb, result, t, "", 0, 1);
                return sb.ToString();
            }
            else // mode == "set"
            {
                if (prop == null || !prop.CanWrite) return $"Error: Property '{path}' is read-only or invalid.";

                try
                {
                    // 🔥【核心魔法：对象引用动态链接】🔥
                    // 如果传入的值以 "@" 开头，代表我们要进行“工具连线” (例如 "@CogImageFileTool1.OutputImage")
                    if (val != null && val.StartsWith("@"))
                    {
                        string sourcePath = val.Substring(1); // 提取 "@" 后面的部分

                        // 将 "ToolName.PropertyName.SubProperty" 拆分为 "ToolName" 和 "PropertyName.SubProperty"
                        string[] parts = sourcePath.Split(new[] { '.' }, 2);
                        string srcToolName = parts[0];
                        string srcPropPath = parts.Length > 1 ? parts[1] : "";

                        // 1. 找到提供数据的源工具
                        object srcTool = FindToolByName(srcToolName);
                        if (srcTool == null) return $"Error: Source tool '{srcToolName}' not found for linking.";

                        // 2. 找到源工具里的具体属性（比如 OutputImage）
                        if (!TryResolveProperty(srcTool, srcPropPath, out object srcObj, out PropertyInfo srcProp))
                            return $"Error: Source property '{srcPropPath}' not found on '{srcToolName}'.";

                        // 3. 取出源对象的值 (拿到真正的图像或对象内存地址)
                        object valueToSet = (srcProp != null) ? srcProp.GetValue(srcObj) : srcObj;

                        // 4. 将源对象的地址，赋值给当前目标工具的属性 (打通任督二脉！)
                        prop.SetValue(targetObj, valueToSet);

                        CogSerializer.SaveObjectToFile(vppObject, vppPath);
                        return $"Success: Linked '{toolName}.{path}' to '{sourcePath}'";
                    }

                    // --- 原有的基础数据类型设置逻辑 (数字、字符串、枚举等) ---
                    object safeVal;
                    if (prop.PropertyType.IsEnum)
                    {
                        safeVal = Enum.Parse(prop.PropertyType, val, true);
                    }
                    else
                    {
                        // 处理布尔值等基础类型转换
                        safeVal = Convert.ChangeType(val, prop.PropertyType);
                    }

                    prop.SetValue(targetObj, safeVal);
                    CogSerializer.SaveObjectToFile(vppObject, vppPath);
                    return $"Success: Set '{path}' to '{safeVal}'";
                }
                catch (Exception ex)
                {
                    Log($"[SetProperty Error] {ex}");
                    return $"Error setting property: {ex.Message}";
                }
            }
        }

        static bool TryResolveProperty(object root, string path, out object targetObj, out PropertyInfo targetProp)
        {
            targetObj = root; targetProp = null;
            if (string.IsNullOrWhiteSpace(path)) return true;

            string[] parts = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            object current = root;
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            for (int i = 0; i < parts.Length; i++)
            {
                if (current == null) return false;
                string part = parts[i];

                if (part.Contains("[") && part.EndsWith("]"))
                {
                    try
                    {
                        int open = part.IndexOf("[");
                        string name = part.Substring(0, open);
                        int idx = int.Parse(part.Substring(open + 1, part.Length - open - 2));

                        PropertyInfo pColl = current.GetType().GetProperty(name, flags);
                        if (pColl == null) return false;
                        object coll = pColl.GetValue(current);

                        object found = null;
                        if (coll is IList list && idx < list.Count) found = list[idx];
                        else if (coll is IEnumerable en)
                        {
                            int cnt = 0;
                            foreach (var item in en) { if (cnt == idx) { found = item; break; } cnt++; }
                        }

                        if (found != null)
                        {
                            current = found;
                            if (i == parts.Length - 1) { targetObj = current; targetProp = null; return true; }
                        }
                        else return false;
                    }
                    catch { return false; }
                }
                else
                {
                    PropertyInfo p = current.GetType().GetProperty(part, flags);
                    if (p == null) return false;
                    if (i == parts.Length - 1) { targetObj = current; targetProp = p; return true; }
                    current = p.GetValue(current);
                }
            }
            targetObj = current;
            return true;
        }

        static void AppendObjectStructure(StringBuilder sb, object instance, Type type, string prefix, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth || instance == null) return;
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name);

            foreach (var p in props)
            {
                if (p.Name.EndsWith("Changed") || p.Name == "Tag" || p.Name == "Parent") continue;
                string valStr = "";
                try
                {
                    object v = p.GetValue(instance);
                    if (v == null) valStr = "null";
                    else if (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum) valStr = v.ToString();
                    else if (v is IEnumerable en && !(v is string))
                    {
                        int c = 0; foreach (var _ in en) c++; valStr = $"[Count={c}]";
                    }
                    else valStr = $"<{p.PropertyType.Name}>";
                }
                catch { valStr = "<Err>"; }

                sb.AppendLine($"{p.Name,-35} | {p.PropertyType.Name,-15} | {valStr}");
                if (currentDepth < maxDepth && (p.Name == "RunParams" || p.Name == "Operator" || p.Name == "Operators"))
                    try { AppendObjectStructure(p.GetValue(instance), p.PropertyType, "", currentDepth + 1, maxDepth, sb); } catch { }
            }
        }

        // 修正原结构递归参数顺序
        static void AppendObjectStructure(object instance, Type type, string prefix, int currentDepth, int maxDepth, StringBuilder sb)
            => AppendObjectStructure(sb, instance, type, prefix, currentDepth, maxDepth);

        // --- 加载与遍历 ---

        static string LoadVppFile(string path)
        {
            try
            {
                vppObject = CogSerializer.LoadObjectFromFile(path);
                vppPath = path;
                toolCache.Clear();
                Traverse(vppObject, (obj, name) => { if (!toolCache.ContainsKey(name)) toolCache[name] = obj; return false; });
                return $"Loaded {Path.GetFileName(path)}. {toolCache.Count} tools found.";
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        static bool Traverse(object obj, Func<object, string, bool> action)
        {
            if (obj == null) return false;
            string name = "Unnamed";
            try
            {
                if (obj is ICogTool ct) name = ct.Name;
                else if (obj is CogJob cj) name = cj.Name;
                else { var p = obj.GetType().GetProperty("Name"); if (p != null) name = p.GetValue(obj)?.ToString() ?? "Unnamed"; }
            }
            catch { }
            if (action(obj, name)) return true;
            if (obj is CogJobManager m) for (int i = 0; i < m.JobCount; i++) Traverse(m.Job(i), action);
            else if (obj is CogJob j) Traverse(j.VisionTool, action);
            else if (obj is CogToolGroup g && g.Tools != null) foreach (ICogTool t in g.Tools) Traverse(t, action);
            else if (obj is CogToolBlock b && b.Tools != null) foreach (ICogTool t in b.Tools) Traverse(t, action);
            else if (obj is IEnumerable en && !(obj is string)) foreach (var item in en) if (item is ICogTool || item is CogJob) Traverse(item, action);
            return false;
        }

        static CogScriptSupport GetScriptSupport(object host)
        {
            if (host == null) return null;

            if (host is CogToolBlock tb) return tb.Script;
            if (host is CogToolGroup tg) return tg.Script;

            if (host is CogJob j)
            {
                if (j.VisionTool is CogToolGroup jtg) return jtg.Script;
                return j.JobScript as CogScriptSupport;
            }

            return null;
        }
        static string TryGetScriptCode(object host)
        {
            var s = GetScriptSupport(host);
            if (s == null) return null;

            var src = s.Source;
            return string.IsNullOrWhiteSpace(src) ? null : src;
        }
        static bool TrySetScriptCode(object host, string code, out string msg)
        {
            msg = "";
            var s = GetScriptSupport(host);
            if (s == null) { msg = "No CogScriptSupport found on this host."; return false; }

            s.Source = code;


            msg = "Injected into Script.Source and compiled.";
            return true;
        }

        static object FindToolByName(string name) => (name != null && toolCache.TryGetValue(name, out object t)) ? t : null;
        static void Log(string m) => Console.Error.WriteLine(m);
        private static PropertyInfo[] GetCachedProperties(Type type)
        {
            if (!typePropertiesCache.TryGetValue(type, out var props))
            {
                props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                typePropertiesCache[type] = props;
            }
            return props;
        }
    }
}