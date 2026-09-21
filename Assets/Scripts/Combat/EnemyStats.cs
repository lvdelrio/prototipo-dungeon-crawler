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

        // La mayoria de los enemigos comparte 2 debilidades (ver EnemyFactory: fuego + corte, para
        // que el jugador aprenda rapido que esas 2 opciones casi siempre funcionan), pero unos
        // pocos rompen a proposito el patron con debilidades propias distintas -- asi hay que
        // fijarse en cada enemigo en vez de spamear siempre lo mismo.
        public Element[] Weaknesses = Array.Empty<Element>();
        public Element Resistance;

        public bool IsWeakTo(Element element) => element != Element.None && Array.IndexOf(Weaknesses, element) >= 0;

        // Barra de "aguante": se gasta con cada golpe recibido (el mismo numero de dano que se le
        // hace a la vida) y al llegar a 0 el enemigo queda "roto" (IsBroken): pierde su proximo
        // turno y despues se recupera entera. Si MaxPoise queda en 0 (default), el enemigo no tiene
        // esta mecanica (nunca se rompe) -- lo usan EnemyFactory.Create* para habilitarla.
        public int MaxPoise;
        public int Poise;
        public bool IsBroken;

        // Si no es null, la PRIMERA vez que este enemigo muere se lo reemplaza (ademas de quedar
        // "derrotado" el mismo) por los enemigos que devuelva esta funcion (p.ej. un Slime grande
        // se divide en 2 Slime chicos). Se limpia despues de usarse una vez, para que los
        // reemplazos no vuelvan a dividirse en cadena.
        public Func<List<EnemyStats>>? OnDeathSplit;

        public bool IsAlive => HP > 0;
    }
}
