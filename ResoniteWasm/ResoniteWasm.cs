using HarmonyLib; // HarmonyLib comes included with a ResoniteModLoader install
using ResoniteModLoader;
using ResoniteHotReloadLib;
using ResoniteEasyFunctionWrapper;
using FrooxEngine;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Wasmtime;
using static Elements.Core.FileUtil;
using Elements.Core;
using FrooxEngine.ProtoFlux;
using System.Reflection;

namespace ResoniteEasyFunctionWrapperExampleMod
{

    // Class name can be anything
    public class ExportedWasmFunctions
    {

        static void AttachValueKindToSlot(ValueKind valueKind, Slot slot, string kindKey, string resoniteTypeKey)
        {
            bool isResoniteType;
            Type resoniteType;
            string valueKindStr = ValueKindToResoniteTypeString(valueKind, out isResoniteType, out resoniteType);

            AttachDynvarVar(
                slotAttachingTo: slot,
                value: valueKind.ToString(),
                fieldName: kindKey
            );

            AttachDynvarVar(
                slotAttachingTo: slot,
                value: resoniteType,
                fieldName: resoniteTypeKey
            );
        }

        static string ValueKindToResoniteTypeString(ValueKind valueKind, out bool isResoniteType, out Type resoniteType)
        {
            isResoniteType = true;
            resoniteType = typeof(string);
            switch (valueKind)
            {
                case ValueKind.AnyRef: isResoniteType = false; return "AnyRef";
                case ValueKind.ExternRef: isResoniteType = false; return "ExternRef";
                case ValueKind.FuncRef: isResoniteType = false; return "FuncRef";
                case ValueKind.Float32: resoniteType = typeof(System.Single); return "float";
                case ValueKind.Float64: resoniteType = typeof(System.Double); return "double";
                case ValueKind.Int32: resoniteType = typeof(System.Int32); return "int";
                case ValueKind.Int64: resoniteType = typeof(System.Int64); return "long";
                case ValueKind.V128: isResoniteType = false; return "vector";
            }
            throw new ArgumentException("Invalid value kind " + valueKind);
        }

        static Slot CreateEmptyInUserspace(string slotName)
        {
            return FrooxEngine.Engine.Current.WorldManager.FocusedWorld.LocalUserSpace.AddSlot(slotName);
        }

        static string WASM_IMPORT_SPACE = "WasmImport";

        static string WASM_EXPORT_SPACE = "WasmExport";

        static string FUNCTION_INFO_SPACE = "WasmFunctionInfo";

        static Component AttachDynvarVar(Slot slotAttachingTo, Object value, string fieldName)
        {
            if (value != null)
            {
                return AttachDynvarVar(
                                slotAttachingTo: slotAttachingTo,
                                type: value.GetType(),
                                value: value,
                                fieldName: fieldName);
            }
            else
            {
                Msg("Error: Cannot attach dynvar when value is null and no type provided, given field name " + fieldName);
                return null;
            }            
        }
        static Component AttachDynvarVar(Slot slotAttachingTo, Type type, Object value, string fieldName)
        {
            Type attachType = typeof(DynamicValueVariable<>).MakeGenericType(type);
            if (!attachType.IsValidGenericType(validForInstantiation: true))
            {
                attachType = typeof(DynamicReferenceVariable<>).MakeGenericType(type);
            }

            Component attachedComponent = slotAttachingTo.AttachComponent(attachType);
            Sync<String> varNameField = (Sync<String>)attachedComponent.GetType().GetField("VariableName").GetValue(attachedComponent);
            varNameField.Value = fieldName;

            var valueField = attachedComponent.GetType().GetField("Value").GetValue(attachedComponent);
            valueField.GetType().GetProperty("Value").SetValue(valueField, value);

            return attachedComponent;
        }

        static string ImportName(string name)
        {
            return WASM_IMPORT_SPACE + "/" + name;
        }

        static void AttachImport(Slot holder, Wasmtime.Import import, Slot functionImports, Slot globalImports, Slot memoryImports, Slot tableImports)
        {
            Slot importSlot = holder.AddSlot(import.Name);
            DynamicVariableSpace importSpace = importSlot.AttachComponent<DynamicVariableSpace>();
            importSpace.SpaceName.Value = WASM_IMPORT_SPACE;

            DynamicValueVariable<string> typeVar = (DynamicValueVariable<string>)AttachDynvarVar(
                slotAttachingTo: importSlot,
                value: "",
                fieldName: ImportName("type")
            );

            AttachDynvarVar(
                slotAttachingTo: importSlot,
                value: import.Name,
                fieldName: ImportName("name")
            );

            if (import.GetType() == typeof(Wasmtime.FunctionImport))
            {
                typeVar.Value.Value = "Function";
                importSlot.SetParent(functionImports);
                Wasmtime.FunctionImport functionImport = (Wasmtime.FunctionImport)import;

                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: functionImport.Parameters.Count,
                    fieldName: ImportName("numParameters")
                );

                for (int i = 0; i < functionImport.Parameters.Count; i++)
                {
                    AttachValueKindToSlot(functionImport.Parameters[i], importSlot,
                        ImportName("parameterKind" + i),
                        ImportName("parameterKindResoniteType" + i));
                }

                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: functionImport.Results.Count,
                    fieldName: ImportName("numResults")
                );

                for (int i = 0; i < functionImport.Results.Count; i++)
                {
                    AttachValueKindToSlot(functionImport.Results[i], importSlot,
                        ImportName("resultKind" + i),
                        ImportName("resultKindResoniteType" + i));
                }
            }
            else if(import.GetType() == typeof(Wasmtime.GlobalImport))
            {
                typeVar.Value.Value = "Global";

                importSlot.SetParent(globalImports);
                Wasmtime.GlobalImport globalImport = (Wasmtime.GlobalImport)import;

                AttachValueKindToSlot(globalImport.Kind, importSlot,
                    ImportName("kind"),
                    ImportName("kindResoniteType"));

                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: globalImport.Mutability == Mutability.Mutable,
                    fieldName: ImportName("mutable")
                );
            }
            else if(import.GetType() == typeof(Wasmtime.MemoryImport))
            {
                typeVar.Value.Value = "Memory";
                importSlot.SetParent(memoryImports);
                Wasmtime.MemoryImport memoryImport = (Wasmtime.MemoryImport)import;

                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: memoryImport.Minimum,
                    fieldName: ImportName("min")
                );

                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: memoryImport.Maximum.HasValue,
                    fieldName: ImportName("hasMax")
                );

                if (memoryImport.Maximum.HasValue)
                {
                    AttachDynvarVar(
                        slotAttachingTo: importSlot,
                        value: memoryImport.Maximum,
                        fieldName: ImportName("max")
                    );
                }
                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: memoryImport.Is64Bit,
                    fieldName: ImportName("is64Bit")
                );
            }
            else if(import.GetType() == typeof(Wasmtime.TableImport))
            {
                typeVar.Value.Value = "Table";
                importSlot.SetParent(tableImports);
                Wasmtime.TableImport tableImport = (Wasmtime.TableImport)import;
                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: tableImport.Minimum,
                    fieldName: ImportName("min")
                );

                AttachDynvarVar(
                    slotAttachingTo: importSlot,
                    value: tableImport.Maximum,
                    fieldName: ImportName("max")
                );

                AttachValueKindToSlot(tableImport.Kind, importSlot,
                    ImportName("kind"),
                    ImportName("kindResoniteType"));
            }
        }

        static string ExportName(string name)
        {
            return WASM_EXPORT_SPACE + "/" + name;
        }

        static string FunctionInfoName(string name)
        {
            return FUNCTION_INFO_SPACE + "/" + name;
        }

        static void AttachExport(Slot holder, Wasmtime.Export export, Slot functionExports, Slot globalExports, Slot memoryExports, Slot tableExports)
        {
            Slot exportSlot = holder.AddSlot(export.Name);
            DynamicVariableSpace exportSpace = exportSlot.AttachComponent<DynamicVariableSpace>();
            exportSpace.SpaceName.Value = WASM_EXPORT_SPACE;

            DynamicValueVariable<string> typeVar = (DynamicValueVariable<string>)AttachDynvarVar(
                slotAttachingTo: exportSlot,
                value: "",
                fieldName: ExportName("type")
            );

            AttachDynvarVar(
                slotAttachingTo: exportSlot,
                value: export.Name,
                fieldName: ExportName("name")
            );

            if (export.GetType() == typeof(Wasmtime.FunctionExport))
            {
                typeVar.Value.Value = "Function";
                exportSlot.SetParent(functionExports);
                Wasmtime.FunctionExport functionExport = (Wasmtime.FunctionExport)export;

                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: functionExport.Parameters.Count,
                    fieldName: ExportName("numParameters")
                );

                for (int i = 0; i < functionExport.Parameters.Count; i++)
                {
                    AttachValueKindToSlot(functionExport.Parameters[i], exportSlot,
                        ExportName("parameterKind" + i),
                        ExportName("parameterKindResoniteType" + i));
                }

                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: functionExport.Results.Count,
                    fieldName: ExportName("numResults")
                );

                for (int i = 0; i < functionExport.Results.Count; i++)
                {
                    AttachValueKindToSlot(functionExport.Results[i], exportSlot,
                        ExportName("resultKind" + i),
                        ExportName("resultKindResoniteType" + i));
                }
            }
            else if (export.GetType() == typeof(Wasmtime.GlobalExport))
            {
                typeVar.Value.Value = "Global";

                exportSlot.SetParent(globalExports);
                Wasmtime.GlobalExport globalExport = (Wasmtime.GlobalExport)export;

                AttachValueKindToSlot(globalExport.Kind, exportSlot,
                    ExportName("kind"),
                    ExportName("kindResoniteType"));

                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: globalExport.Mutability == Mutability.Mutable,
                    fieldName: ExportName("mutable")
                );
            }
            else if (export.GetType() == typeof(Wasmtime.MemoryExport))
            {
                typeVar.Value.Value = "Memory";
                exportSlot.SetParent(memoryExports);
                Wasmtime.MemoryExport memoryImport = (Wasmtime.MemoryExport)export;

                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: memoryImport.Minimum,
                    fieldName: ExportName("min")
                );

                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: memoryImport.Maximum.HasValue,
                    fieldName: ExportName("hasMax")
                );

                if (memoryImport.Maximum.HasValue)
                {
                    AttachDynvarVar(
                        slotAttachingTo: exportSlot,
                        value: memoryImport.Maximum,
                        fieldName: ExportName("max")
                    );
                }
                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: memoryImport.Is64Bit,
                    fieldName: ExportName("is64Bit")
                );
            }
            else if (export.GetType() == typeof(Wasmtime.TableExport))
            {
                typeVar.Value.Value = "Table";
                exportSlot.SetParent(tableExports);
                Wasmtime.TableExport tableExport = (Wasmtime.TableExport)export;
                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: tableExport.Minimum,
                    fieldName: ExportName("min")
                );

                AttachDynvarVar(
                    slotAttachingTo: exportSlot,
                    value: tableExport.Maximum,
                    fieldName: ExportName("max")
                );

                AttachValueKindToSlot(tableExport.Kind, exportSlot,
                    ExportName("kind"),
                    ExportName("kindResoniteType"));
            }
        }

        static void GetWasmImports(Slot holder, Wasmtime.Module module)
        {
            Slot functionImports = holder.AddSlot("Function");
            Slot globalsImports = holder.AddSlot("Global");
            Slot memoryImports = holder.AddSlot("Memory");
            Slot tableImports = holder.AddSlot("Table");
            foreach (Wasmtime.Import import in module.Imports)
            {
                AttachImport(
                    holder: holder,
                    import: import,
                    functionImports: functionImports,
                    globalImports: globalsImports, 
                    memoryImports: memoryImports,
                    tableImports: tableImports
                );
            }
        }

        static void GetWasmExports(Slot holder, Wasmtime.Module module)
        {
            Slot functionExports = holder.AddSlot("Function");
            Slot globalsExports = holder.AddSlot("Global");
            Slot memoryExports = holder.AddSlot("Memory");
            Slot tableExports = holder.AddSlot("Table");
            foreach (Wasmtime.Export export in module.Exports)
            {
                AttachExport(
                    holder: holder,
                    export: export,
                    functionExports: functionExports,
                    globalExports: globalsExports,
                    memoryExports: memoryExports,
                    tableExports: tableExports
                );
            }

        }

        public static Slot GetWasmInfo(Wasmtime.Module module)
        {
            Msg("Got module " + module);
            // what u doin
            if (module == null)
            {
                return null;
            }
            Slot infoHolder = CreateEmptyInUserspace(module.Name);
            Slot imports = infoHolder.AddSlot("Imports");
            GetWasmImports(imports, module);
            Slot exports = infoHolder.AddSlot("Exports");
            GetWasmExports(exports, module);
            return infoHolder;
        }

        private static void Msg(string message)
        {
            ResoniteMod.Msg(message);
        }

        public static bool IsNull(string guid)
        {
            return guid == Guid.Empty.ToString();
        }

        private static Wasmtime.Engine _engine;

        private static Wasmtime.Engine engine {
            get
            {
                if (_engine == null) {
                    _engine = new Wasmtime.Engine();
                }
                return _engine;
            }
        }

        public static void
            CreateWasmLinker(out Wasmtime.Linker linker)
        {
            linker = new Wasmtime.Linker(engine);
        }

        public static void CreateWasmStore(out Wasmtime.Store store)
        {
            store = new Wasmtime.Store(engine);
        }

        public static Wasmtime.Instance InstantiateModule(Wasmtime.Linker linker, Wasmtime.Store store, Wasmtime.Module module)
        {
            return linker.Instantiate(store, module);
        }

        public static Action GetAction(Wasmtime.Instance instance, string name, out bool actionExists)
        {
            Msg("got instance " + instance + " and name " + name);
            Action action = instance.GetAction(name);
            actionExists = false;
            if (action != null)
            {
                Msg(action.GetType().ToString());
                actionExists = true;
            }
            return action;
        }

        public static Wasmtime.Function GetFunction(Wasmtime.Instance instance, string name, out bool functionExists)
        {
            Msg("got instance " + instance + " and name " + name);
            Wasmtime.Function function = instance.GetFunction(name);
            functionExists = false;
            if (function != null)
            {
                Msg(function.GetType().ToString());
                functionExists = true;
            }
            return function;
        }



        public static Slot GetFunctionInfo(Wasmtime.Function function)
        {
            Slot infoSlot = CreateEmptyInUserspace("FunctionInfo");
            DynamicVariableSpace space = infoSlot.AttachComponent<DynamicVariableSpace>();
            space.SpaceName.Value = FUNCTION_INFO_SPACE;

            AttachDynvarVar(
                infoSlot,
                function.IsNull,
                FunctionInfoName("isNull")
            );

            if (!function.IsNull)
            {
                AttachDynvarVar(
                    infoSlot,
                    function.Parameters.Count,
                    FunctionInfoName("numParameters")
                );

                int i = 0;
                foreach (ValueKind param in function.Parameters)
                {
                    AttachValueKindToSlot(
                        param,
                        infoSlot,
                        FunctionInfoName("parameterKind" + i),
                        FunctionInfoName("parameterKindResoniteType" + i)
                    );
                    i += 1;
                }

                AttachDynvarVar(
                    infoSlot,
                    function.Results.Count,
                    FunctionInfoName("numResults")
                );

                i = 0;
                foreach (ValueKind param in function.Results)
                {
                    AttachValueKindToSlot(
                        param,
                        infoSlot,
                        FunctionInfoName("resultKind" + i),
                        FunctionInfoName("resultKindResoniteType" + i)
                    );
                    i += 1;
                }
            }
            return infoSlot;
        }

        // Todo: allocation functions for vectors, memory, functions, tables, and global refs


        public static Slot GetFunctionCallerTemplateData(Wasmtime.Function function)
        {
            Slot functionCallerSlot = CreateEmptyInUserspace("FunctionCallerData");
            DynamicVariableSpace space = functionCallerSlot.AttachComponent<DynamicVariableSpace>();
            
            AttachDynvarVar(
                functionCallerSlot,
                function.Parameters.Count,
                "numParameters"
            );

            int i = 0;
            foreach (ValueKind inParam in function.Parameters)
            {
                bool isResoniteType;
                Type resoniteType;
                ValueKindToResoniteTypeString(inParam, out isResoniteType, out resoniteType);
                AttachDynvarVar(
                    slotAttachingTo: functionCallerSlot,
                    value: inParam.ToString(),
                    fieldName: "parameterKind" + i
                );
                AttachDynvarVar(
                    slotAttachingTo: functionCallerSlot,
                    type: resoniteType,
                    value: resoniteType.GetDefault(),
                    fieldName: FunctionInfoName("parameter" + i)
                );
                i += 1;
            }

            AttachDynvarVar(
                functionCallerSlot,
                function.Results.Count,
                "numResults"
            );

            foreach (ValueKind outParam in function.Results)
            {
                bool isResoniteType;
                Type resoniteType;
                ValueKindToResoniteTypeString(outParam, out isResoniteType, out resoniteType);
                AttachDynvarVar(
                    slotAttachingTo: functionCallerSlot,
                    value: outParam.ToString(),
                    fieldName: "resultKind" + i
                );
                AttachDynvarVar(
                    slotAttachingTo: functionCallerSlot,
                    type: resoniteType,
                    value: resoniteType.GetDefault(),
                    fieldName: FunctionInfoName("result" + i)
                );
                i += 1;
            }

            return functionCallerSlot;
        }




        // What I'd like is to automatically generate a protoflux node calling the function
        // However a simpler option is to have a node to make inputs, node to call, node to fetch outputs
        // Though if I'm already making dynvars, why not just dynvar this instead?
        // Can make a dynvar with all the input and output values
        // Then pass that in

        public static async Task<Wasmtime.Module> LoadWasmFile(StaticBinary staticBinary, FileMetadata fileMetadata)
        {
            Wasmtime.Module module = null;
            Uri uri = staticBinary.URL;
            if (uri != null)
            {
                await default(ToBackground);
                string filePath = await FrooxEngine.Engine.Current.AssetManager.GatherAssetFile(uri, 100.0f);
                if (filePath != null)
                {
                    if (fileMetadata.Filename.Value.ToLower().EndsWith(".wat"))
                    {
                        module = Wasmtime.Module.FromTextFile(engine, filePath);
                    }
                    if (module == null || fileMetadata.Filename.Value.ToLower().EndsWith(".wasm"))
                    {
                        module = Wasmtime.Module.FromFile(engine, filePath);
                    }
                }
                await default(ToWorld);
            }
            return module;
        }
    }

    public class ResoniteWasm : ResoniteMod
    {
        public override string Name => "ResoniteWasm";
        public override string Author => "TessaCoil";
        public override string Version => "1.0.0"; //Version of the mod, should match the AssemblyVersion
        public override string Link => "https://github.com/Phylliida/ResoniteWasm"; // Optional link to a repo where this mod would be located

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> enabled = new ModConfigurationKey<bool>("enabled", "Should the mod be enabled", () => true); //Optional config settings

        private static ModConfiguration Config; //If you use config settings, this will be where you interface with them.
        private static string harmony_id = "bepis.TessaCoil.ResoniteWasm";

        private static Harmony harmony;

        public override void OnEngineInit()
        {
            HotReloader.RegisterForHotReload(this);

            Config = GetConfiguration(); //Get the current ModConfiguration for this mod
            Config.Save(true); //If you'd like to save the default config values to file
        
            SetupMod();
        }

        public static void SetupMod()
        {
            ResoniteEasyFunctionWrapper.ResoniteEasyFunctionWrapper.WrapClass(
                typeof(ExportedWasmFunctions),
                modNamespace: harmony_id);
        }

        static void BeforeHotReload()
        {
            // Remove menus and class wrappings
            ResoniteEasyFunctionWrapper.ResoniteEasyFunctionWrapper.UnwrapClass(
                classType:typeof(ExportedWasmFunctions),
                modNamespace: harmony_id);
        }

        static void OnHotReload(ResoniteMod modInstance)
        {
            // Get the config
            Config = modInstance.GetConfiguration();

            // Now you can setup your mod again
            SetupMod();
        }
    }
}
