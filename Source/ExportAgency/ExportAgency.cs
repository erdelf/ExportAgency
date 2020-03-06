using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace ExportAgency
{
    using System.Reflection;
    using System.Reflection.Emit;

    public enum ExportType : byte
    {
        BILL,
        STORAGE_SETTINGS,
        OUTFIT,
        DRUGPOLICY
    }

    [StaticConstructorOnStartup]
    public class ExportAgency
    {
        static ExportAgency()
        {
            Harmony harmony     = new Harmony(id: "rimworld.erdelf.exportAgency");
            HarmonyMethod   exportGizmo = new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(ExportGizmos));

            #region Bills


            harmony.Patch(original: AccessTools.Method(type: typeof(ThingWithComps), name: nameof(Thing.GetGizmos)), prefix: null, postfix: exportGizmo);

            harmony.Patch(original: AccessTools.Method(type: typeof(Bill), name: nameof(Bill.GetUniqueLoadID)), prefix: null,
                postfix: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(BillUniqueIdPostfix)));

            #endregion

            #region StorageSettings

            foreach (Type t in GenTypes.AllTypes.Where(predicate: t =>
                                                                      t.GetInterfaces().Contains(value: typeof(IStoreSettingsParent)) && !t.IsInterface && !t.IsAbstract))
            {
                MethodInfo original = AccessTools.Method(type: t, name: nameof(Thing.GetGizmos));
                if (original?.DeclaringType == t)
                    harmony.Patch(original: original, prefix: null, postfix: exportGizmo);
            }

        #endregion

            #region Outfits

            harmony.Patch(original: AccessTools.Method(type: typeof(Dialog_ManageOutfits), name: nameof(Dialog_ManageOutfits.PreClose)), postfix: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(OutfitDialogClosePostfix)));

            harmony.Patch(original: AccessTools.Constructor(type: typeof(OutfitDatabase)), postfix: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(OutfitDatabasePostfix)));

            #endregion

            #region DrugPolicies

            harmony.Patch(original: AccessTools.Method(type: typeof(Dialog_ManageDrugPolicies), name: nameof(Dialog_ManageDrugPolicies.PreClose)), postfix: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(DrugPolicyDialogClosePostfix)));

            harmony.Patch(original: AccessTools.Constructor(type: typeof(DrugPolicyDatabase)), postfix: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(DrugPolicyDatabasePostfix)));

            harmony.Patch(original: AccessTools.Method(type: typeof(Dialog_ManageDrugPolicies), name: nameof(Dialog_ManageDrugPolicies.DoWindowContents)),
                transpiler: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(DrugPolicyManageTranspiler)));

            harmony.Patch(original: AccessTools.Method(type: typeof(DrugPolicyDatabase), name: nameof(DrugPolicyDatabase.DefaultDrugPolicy)),
                postfix: new HarmonyMethod(methodType: typeof(ExportAgency), methodName: nameof(DefaultDrugPolicyPostfix)));
            #endregion

            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            ExportAgencyMod.Settings.GetHashCode();
        }

        public static readonly Texture2D IMPORT_TEXTURE = ContentFinder<Texture2D>.Get(itemPath: "ExportAgency/Import");

        public static readonly Texture2D EXPORT_TEXTURE = ContentFinder<Texture2D>.Get(itemPath: "ExportAgency/Export");

        public static void ExportGizmos(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance is IBillGiver billGiver)
            {
                if (billGiver.BillStack.Bills.Any())
                    __result = __result.AddItem(item: new Command_Action
                    {
                        action = () =>
                                     ExportBillStack(stack: billGiver.BillStack),
                        defaultLabel = "Export",
                        icon         = EXPORT_TEXTURE
                    });
                if (ExportAgencyMod.Settings.dictionary.ContainsKey(key: ExportType.BILL) && 
                    ExportAgencyMod.Settings.dictionary[key: ExportType.BILL].Any(predicate: li => li.exposable.Select(exp => (Bill) exp.exposable).Any(predicate: bi => ((Thing) __instance).def.AllRecipes.Contains(item: bi.recipe))))
                    __result = __result.AddItem(item: new Command_Action
                    {
                        action = () => Find.WindowStack.Add(window: new FloatMenu(options: ExportAgencyMod.Settings.dictionary[key: ExportType.BILL]
                           .Where(predicate: li => li.exposable.Select(exp => (Bill) exp.exposable).Any(predicate: bi => ((Thing) __instance).def.AllRecipes.Contains(item: bi.recipe)))
                           .Select(selector: li => new FloatMenuOption(label: li.exposable.Name, action: () => PasteBillStack(billGiver: billGiver, bills: li.exposable.Select(exp => exp.exposable)))).ToList())),
                        defaultLabel = "Import",
                        icon         = IMPORT_TEXTURE
                    });
            }

            if (__instance is IStoreSettingsParent storeParent)
            {
                if (!storeParent.StorageTabVisible) return;
                __result = __result.AddItem(item: new Command_Action
                {
                    action       = () => ExportStorageSettings(settings: storeParent.GetStoreSettings()),
                    defaultLabel = "Export",
                    icon         = EXPORT_TEXTURE
                });
                if (ExportAgencyMod.Settings.dictionary.ContainsKey(key: ExportType.STORAGE_SETTINGS))
                    __result = __result.AddItem(item: new Command_Action
                    {
                        action = () =>
                        {
                            Find.WindowStack.Add(window: new FloatMenu(options: ExportAgencyMod.Settings.dictionary[key: ExportType.STORAGE_SETTINGS]
                               .Select(selector: li => new FloatMenuOption(label: li.exposable.Name, action: () =>
                                    PasteStorageSettings(storeParent: storeParent, settings: (StorageSettings) li.exposable.First().exposable))).ToList()));
                        },
                        defaultLabel = "Import",
                        icon         = IMPORT_TEXTURE
                    });
            }
        }


        #region Drugs

        // ReSharper disable once RedundantAssignment
        public static void DefaultDrugPolicyPostfix(DrugPolicyDatabase __instance, ref DrugPolicy __result) => 
            __result = __instance.AllPolicies[index: ExportAgencyMod.Settings.defaultDrugPolicyIndex];

        public static IEnumerable<CodeInstruction> DrugPolicyManageTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instructionList = instructions.ToList();
            for (int index = 0; index < instructionList.Count; index++)
            {
                CodeInstruction instruction = instructionList[index: index];

                // ReSharper disable once RedundantCast
                if (index < instructionList.Count - 1 && instructionList[index: index + 1].operand == (object) "DeleteDrugPolicy")
                {
                    yield return new CodeInstruction(opcode: OpCodes.Ldloc_0);
                    yield return new CodeInstruction(opcode: OpCodes.Ldc_R4, operand: 0.0f);
                    yield return new CodeInstruction(opcode: OpCodes.Ldc_R4, operand: 150f);
                    yield return new CodeInstruction(opcode: OpCodes.Ldc_R4, operand: 35f);
                    yield return new CodeInstruction(opcode: OpCodes.Ldarg_0);
                    yield return new CodeInstruction(opcode: OpCodes.Call, 
                        operand: AccessTools.Property(type: typeof(Dialog_ManageDrugPolicies), name: "SelectedPolicy").GetGetMethod(nonPublic: true));
                    yield return new CodeInstruction(opcode: OpCodes.Call, 
                        operand: AccessTools.Method(type: typeof(ExportAgency), name: nameof(NewDefaultDrugPolicy)));
                }

                yield return instruction;
            }
        }

        public static void NewDefaultDrugPolicy(float x, float y, float w, float h, DrugPolicy selected)
        {
            if (selected == null) return;

            if (Widgets.ButtonText(rect: new Rect(x: x+10, y: y, width: w, height: h), label: "NewDefaultDrugPolicy".Translate()))
                ExportAgencyMod.Settings.defaultDrugPolicyIndex = Current.Game.drugPolicyDatabase.AllPolicies.IndexOf(item: selected);
        }

        public static void DrugPolicyDatabasePostfix(DrugPolicyDatabase __instance)
        {
            if (!ExportAgencyMod.Settings.dictionary.ContainsKey(key: ExportType.DRUGPOLICY))
                return;
            __instance.AllPolicies.Clear();
            foreach (ExposableList<IExposable> li in ExportAgencyMod.Settings.dictionary[key: ExportType.DRUGPOLICY])
            {
                DrugPolicy policy = __instance.MakeNewDrugPolicy();
                int x = 0;
                for (int i = 0; i < li.Count; i++)
                    if(li[index: i].exposable is DrugPolicyEntry dpe)
                        if(dpe.drug != null && policy.Count < x-1)
                            policy[index: x++] = dpe;
                policy.label  = li.Name;
            }
        }

        public static void DrugPolicyDialogClosePostfix()
        {
            if (ExportAgencyMod.Settings.dictionary.ContainsKey(key: ExportType.DRUGPOLICY))
                ExportAgencyMod.Settings.dictionary[key: ExportType.DRUGPOLICY].Clear();

            foreach (DrugPolicy policy in Current.Game.drugPolicyDatabase.AllPolicies)
                Export(key: ExportType.DRUGPOLICY, 
                    list: Enumerable.Range(start: 0, count: policy.Count).Select(selector: index => policy[index: index] as IExposable), name: policy.label);
        }

        #endregion

        #region Outfits

        public static void OutfitDatabasePostfix(OutfitDatabase __instance)
        {
            if (!ExportAgencyMod.Settings.dictionary.ContainsKey(key: ExportType.OUTFIT)) return;
            __instance.AllOutfits.Clear();
            foreach (ExposableList<IExposable> li in ExportAgencyMod.Settings.dictionary[key: ExportType.OUTFIT])
            {
                Outfit outfit = __instance.MakeNewOutfit();
                outfit.filter = (ThingFilter) li.First().exposable;
                outfit.label  = li.Name;
            }
        }

        public static void OutfitDialogClosePostfix()
        {
            if (ExportAgencyMod.Settings.dictionary.ContainsKey(key: ExportType.OUTFIT))
                ExportAgencyMod.Settings.dictionary[key: ExportType.OUTFIT].Clear();

            foreach (Outfit outfit in Current.Game.outfitDatabase.AllOutfits)
                Export(key: ExportType.OUTFIT, list: new IExposable[] { outfit.filter }, name: outfit.label);
        }

    #endregion

        #region Bills

        public static void BillUniqueIdPostfix(Bill __instance, ref string __result)
        {
            if (Traverse.Create(root: __instance).Field<int>(name: "loadID").Value == int.MinValue)
                __result = $"{__result}_{nameof(ExportAgency)}_{__instance.GetHashCode()}";
        }

        private static void PasteBillStack(IBillGiver billGiver, IEnumerable<IExposable> bills)
        {
            billGiver.BillStack.Clear();
            foreach (Bill bill in bills.Cast<Bill>().Where(predicate: bi => ((Thing) billGiver).def.AllRecipes.Contains(item: bi.recipe)))
            {
                Bill bi = bill.Clone();
                bi.InitializeAfterClone();
                billGiver.BillStack.AddBill(bill: bi);
            }
        }

        private static void ExportBillStack(BillStack stack) =>
            Find.WindowStack.Add(window: new Dialog_RenameExportName(key: ExportType.BILL, list: stack.Bills.Select(selector: bi =>
            {
                Bill bill = bi.Clone();
                bill.pawnRestriction = null;
                Traverse.Create(root: bill).Field<int>(name: "loadID").Value = int.MinValue;
                return bill;
            }).OfType<IExposable>()));

        #endregion

        #region StorageSettings

        private static void PasteStorageSettings(IStoreSettingsParent storeParent, StorageSettings settings) =>
            storeParent.GetStoreSettings().CopyFrom(other: settings);

        public static void ExportStorageSettings(StorageSettings settings)
        {
            StorageSettings set = new StorageSettings();
            set.CopyFrom(other: settings);
            Find.WindowStack.Add(window: new Dialog_RenameExportName(key: ExportType.STORAGE_SETTINGS,
                list: new List<IExposable> {set}));
        }

        #endregion

        public static void Export(ExportType key, IEnumerable<IExposable> list, string name)
        {
            IEnumerable<IExposable> exposables = list as IExposable[] ?? list.ToArray();
            if (!exposables.Any()) return;

            if (!ExportAgencyMod.Settings.dictionary.ContainsKey(key: key))
                ExportAgencyMod.Settings.dictionary.Add(key: key, value: new ExposableList<ExposableList<IExposable>>());

            ExportAgencyMod.Settings.dictionary[key: key].Add(item: new ExposableList<IExposable>(exposables: exposables) {Name = name});
            ExportAgencyMod.instance.WriteSettings();
        }
    }

    public class ExposableList<T> : List<ExposableListItem<T>>, IExposable where T : IExposable
    {
        public ExposableList() : base(capacity: 1)
        {
        }

        public ExposableList(IEnumerable<T> exposables) : base(collection: exposables.Select(selector: exp => new ExposableListItem<T>(exp)))
        {
        }

        public string Name { get; set; }

        public void ExposeData()
        {
            string name = this.Name;
            Scribe_Values.Look(value: ref name, label: "name");
            this.Name = name;

            List<ExposableListItem<T>> list = this.ListFullCopy();
            Scribe_Collections.Look(list: ref list, label: "internalList");
            this.Clear();
            this.AddRange(collection: list.Where(predicate: exp => exp.resolvable));
        }

        internal void Add(T item) => base.Add(item: new ExposableListItem<T>(exposable: item));
    }

    public class ExposableListItem<T> : IExposable where T : IExposable
    {
        public T exposable;
        public bool resolvable;

        public ExposableListItem()
        {
        }

        public ExposableListItem(T exposable) => this.exposable = exposable;

        public void ExposeData()
        {
            try
            {
                Scribe_Deep.Look(target: ref this.exposable, label: "exposable");
                this.resolvable = this.exposable != null;
            }
            catch
            {
                this.resolvable = false;
            }

            if(!this.resolvable)
                Log.Message(text: $"Found unresolvable {typeof(T).FullName} in exported List");
        }

        public static implicit operator T(ExposableListItem<T> exp) => exp.exposable;
    }

    internal class Dialog_RenameExportName : Dialog_Rename
    {
        private readonly ExportType              key;
        private readonly IEnumerable<IExposable> list;

        public Dialog_RenameExportName(ExportType key, IEnumerable<IExposable> list)
        {
            this.key     = key;
            this.list    = list;
            this.curName = key.ToString() + (ExportAgencyMod.Settings.dictionary.ContainsKey(key: key + 0) ? ExportAgencyMod.Settings.dictionary[key: key].Count : 0);
        }

        protected override void SetName(string name) => ExportAgency.Export(key: this.key, list: this.list, name: name);
    }
}