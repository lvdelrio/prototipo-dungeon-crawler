using System;
using System.Collections.Generic;

namespace Combat
{
    public class EnemyStats
    {
        public string Name = "";
        public int MaxHP;
        public int HP;
        public int Attack;
        public int Defense;
        public int Speed;
        // Chance (0-100) de esquivar por completo un golpe de la party (ver
        // CombatEngine.ComputeDamageVsEnemy) -- 0 por defecto, la mayoria de los enemigos no la usa.
        public int Evasion;
        public Element AttackElement;

        // La mayoria de los enemigos comparte 2 debilidades (ver EnemyFactory: fuego + corte, para
        // que el jugador aprenda rapido que esas 2 opciones casi siempre funcionan), pero unos
        // pocos rompen a proposito el patron con debilidades propias distintas -- asi hay que
        // fijarse en cada enemigo en vez de spamear siempre lo mismo.
        public Element[] Weaknesses = Array.Empty<Element>();
        public Element Resistance;

        public bool IsWeakTo(Element element) => element != Element.None && Array.IndexOf(Weaknesses, element) >= 0;

        // Barra de "aguante": se gasta con cada golpe recibido (un porcentaje del dano de HP de ese
        // golpe, ver CombatEngine.PoiseDamageBasicAttack/PoiseDamageWeaknessHit) y al llegar a 0 el
        // enemigo queda "roto" (IsBroken): pierde su proximo turno y despues se recupera entera. Si
        // MaxPoise queda en 0 (default), el enemigo no tiene esta mecanica (nunca se rompe) -- lo
        // usan EnemyFactory.Create* para habilitarla.
        public int MaxPoise;
        public int Poise;
        public bool IsBroken;

        // Cuanto resiste este enemigo el extra de dano al aguante de un golpe de DEBILIDAD (ver
        // CombatEngine.PoiseDamageWeaknessHit): 1 = igual que cualquier enemigo, mas alto = ese
        // multiplicador pesa menos sobre SU aguante en particular (divide, no resta). Los jefes lo
        // usan (ver EnemyFactory.CreateBoss) porque combinado con que un golpe de debilidad YA
        // pega el doble de dano de HP, el aguante se les rompia en 1-2 golpes bien apuntados --
        // siguen siendo vulnerables a las debilidades, solo que no de forma tan desproporcionada.
        public float PoiseWeaknessResistance = 1f;

        // Debuff temporal de Defensa (rondas completas, se descuenta en
        // CombatEngine.BuildTurnOrder) -- lo usa la habilidad versatil del Trovador cuando se tira
        // sobre un enemigo en vez de un aliado. Positivo = cuanto se le resta a la Defensa real.
        public int DefenseDebuffAmount;
        public int DefenseDebuffRoundsLeft;
        public int EffectiveDefense => Math.Max(0, Defense - DefenseDebuffAmount);

        // Dano sostenido (veneno o sangrado -- mismo mecanismo estandarizado para los dos, ver
        // Combat/EquipmentItem.OnHitStatusName, solo cambia el nombre que se muestra en el log):
        // lo aplica un arma equipada con chance al conectar un golpe (ver
        // CombatEngine.ApplyDamageToEnemy). Tickea DamagePerRound cada ronda nueva
        // (CombatEngine.BuildTurnOrder) hasta que RoundsLeft llega a 0.
        public int DotRoundsLeft;
        public int DotDamagePerRound;
        public string DotLabel;

        // Si no es null, la PRIMERA vez que este enemigo muere se lo reemplaza (ademas de quedar
        // "derrotado" el mismo) por los enemigos que devuelva esta funcion (p.ej. un Slime grande
        // se divide en 2 Slime chicos). Se limpia despues de usarse una vez, para que los
        // reemplazos no vuelvan a dividirse en cadena.
        public Func<List<EnemyStats>>? OnDeathSplit;

        public bool IsAlive => HP > 0;
    }
}
