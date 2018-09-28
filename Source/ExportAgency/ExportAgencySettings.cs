using System;
using System.Collections.Generic;
using System.Linq;

namespace ExportAgency
{
    using RimWorld;
    using UnityEngine;
    using Verse;

    public class ExportSettings : ModSettings
    {
        public Dictionary<ExportType, ExposableList<ExposableList<IExposable>>> dictionary = new Dictionary<ExportType, ExposableList<ExposableList<IExposable>>>();
        public int                                                              defaultDrugPolicyIndex;


        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(dict: ref this.dictionary, label: "exposeDictionary", valueLookMode: LookMode.Deep);
            Scribe_Values.Look(value: ref this.defaultDrugPolicyIndex, label: "defaultDrugPolicyIndex");
        }
    }

    public class ExportAgencyMod : Mod
    {
        private static ExportSettings  settings;
        public static  ExportAgencyMod instance;

        public override string SettingsCategory() => "Export Agency";

        public static ExportSettings Settings => settings ?? (settings = instance.GetSettings<ExportSettings>());

        public ExportAgencyMod(ModContentPack content) : base(content: content) => instance = this;

        private Vector2 scrollPosition;

        private ExportType currentExportType;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if(Widgets.ButtonText(rect: inRect.TopPart(pct: 0.05f).LeftPart(pct: 0.7f).RightPart(pct: 0.5f), label: this.currentExportType.ToString()))
                Find.WindowStack.Add(window: new FloatMenu(options: Enum.GetValues(enumType: typeof(ExportType)).Cast<ExportType>().Select(selector: t => 
                    new FloatMenuOption(label: t.ToString(), action: () => this.currentExportType = t)).ToList()));
            if (!Settings.dictionary.ContainsKey(key: this.currentExportType)) return;

            ExposableList<ExposableList<IExposable>> exposableList = Settings.dictionary[key: this.currentExportType];

            Widgets.BeginScrollView(outRect: inRect.BottomPart(pct: 0.9f).TopPart(pct: 0.9f), scrollPosition: ref this.scrollPosition,
                viewRect: new Rect(x: inRect.x, y: inRect.y, width: inRect.width-18f, height: (exposableList.Count+1) * 25));

            Log.Message(exposableList.First().Name + " | " + exposableList.Last().Name);

            for (int i = 2; i < exposableList.Count+2; i++)
            {
                Widgets.DrawLineHorizontal(x: 0, y: i * 25f, length: inRect.width-18f);
                Widgets.Label(rect: new Rect(x: 0, y: i * 25f + 2.5f, width: inRect.width *0.3f, height: 20f), 
                    label: exposableList[index: i-2].Name);
                if (!Widgets.ButtonImage(butRect: new Rect(x: inRect.width - 18f - 20f, y: i * 25f + 2.5f, width: 20f, height: 20f),
                        tex: TexCommand.RemoveRoutePlannerWaypoint)) continue;
                exposableList.Remove(item: exposableList[index: i-2]);
                this.WriteSettings();
            }
            Widgets.EndScrollView();
        }
    }
}
