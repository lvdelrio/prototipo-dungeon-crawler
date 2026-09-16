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
        public Element AttackElement;
        public Element Weakness;
        public Element Resistance;

        // Si no es null, la PRIMERA vez que este enemigo muere se lo reemplaza (ademas de quedar
        // "derrotado" el mismo) por los enemigos que devuelva esta funcion (p.ej. un Slime grande
        // se divide en 2 Slime chicos). Se limpia despues de usarse una vez, para que los
        // reemplazos no vuelvan a dividirse en cadena.
        public Func<List<EnemyStats>>? OnDeathSplit;

        public bool IsAlive => HP > 0;
    }
}
