using UnityEditor;
using UnityEngine;
using Meta;
using Gameplay;
using Combat;

// Valida en modo batch la parte que es genuinamente especifica de Unity del sistema de
// meta-progresion (JsonUtility + guardado en disco vía MetaSaveService); la logica pura
// (puntos por run, costo/nivel de mejoras, ApplyUpgradesToParty, TryDrillWall) ya se valido a
// fondo en el harness de consola C:\temp\CombatTest y C:\temp\DungeonGenTest antes de portarla.
public static class MetaBatchValidator
{
    [MenuItem("Dungeon/Validate Meta Save (Batch Log)")]
    public static void ValidateFromBatch()
    {
        bool allOk = true;

        var original = new MetaProgress
        {
            BankedPoints = 123,
            MapCharges = 2,
            DrillCharges = 1,
        };
        original.TryPurchaseUpgrade(CharacterClass.Warrior, UpgradeStat.Attack);
        original.TryPurchaseUpgrade(CharacterClass.Warrior, UpgradeStat.MaxHp);
        original.TryPurchaseUpgrade(CharacterClass.Medic, UpgradeStat.MaxTp);

        MetaSaveService.Save(original);
        var loaded = MetaSaveService.Load();

        allOk &= Check("BankedPoints sobrevive el guardado", loaded.BankedPoints == original.BankedPoints, $"esperado={original.BankedPoints} real={loaded.BankedPoints}");
        allOk &= Check("MapCharges/DrillCharges sobreviven el guardado", loaded.MapCharges == original.MapCharges && loaded.DrillCharges == original.DrillCharges);
        allOk &= Check("Cantidad de upgrades guardados coincide", loaded.Upgrades.Count == original.Upgrades.Count, $"esperado={original.Upgrades.Count} real={loaded.Upgrades.Count}");
        allOk &= Check("Nivel de Attack del Warrior sobrevive", loaded.GetUpgradeLevel(CharacterClass.Warrior, UpgradeStat.Attack) == 1);
        allOk &= Check("Nivel de MaxHp del Warrior sobrevive", loaded.GetUpgradeLevel(CharacterClass.Warrior, UpgradeStat.MaxHp) == 1);
        allOk &= Check("Nivel de MaxTp del Medic sobrevive", loaded.GetUpgradeLevel(CharacterClass.Medic, UpgradeStat.MaxTp) == 1);
        allOk &= Check("Clase sin upgrades comprados queda en nivel 0", loaded.GetUpgradeLevel(CharacterClass.Ranger, UpgradeStat.Attack) == 0);

        // ApplyUpgradesToParty debe reflejar lo cargado desde disco (no solo lo que quedo en memoria).
        var party = PartyFactory.CreateDefaultParty();
        int baseWarriorAttack = party.Find(p => p.Class == CharacterClass.Warrior).Attack;
        loaded.ApplyUpgradesToParty(party);
        var warrior = party.Find(p => p.Class == CharacterClass.Warrior);
        allOk &= Check("ApplyUpgradesToParty con datos cargados de disco sube el Attack del Warrior en 1", warrior.Attack == baseWarriorAttack + 1, $"esperado={baseWarriorAttack + 1} real={warrior.Attack}");

        if (allOk)
            Debug.Log("[META-VALIDATE] RESULTADO FINAL: TODO OK");
        else
            Debug.LogError("[META-VALIDATE] RESULTADO FINAL: HAY FALLOS");

        if (!allOk) EditorApplication.Exit(1);
    }

    private static bool Check(string name, bool ok, string detail = "")
    {
        if (ok)
            Debug.Log($"[META-VALIDATE] OK - {name}");
        else
            Debug.LogError($"[META-VALIDATE] FALLO - {name} {detail}");
        return ok;
    }
}
