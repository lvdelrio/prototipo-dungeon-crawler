# Prototipo Dungeon Crawler

Prototipo hecho en Unity de un dungeon crawler en primera persona por turnos, inspirado en la
generación de mapas de **Etrian Odyssey** y fusionado con el combate elemental al estilo
**Persona**. Es un proyecto de práctica/aprendizaje, en desarrollo activo.

## De qué se trata

Explorás una mazmorra cuadriculada en primera persona, piso por piso, mientras el peligro
acumulado de cada paso puede disparar un encuentro. Ahí el juego pasa a una escena de combate por
turnos con una party fija de 6 personajes de clases distintas contra los enemigos del piso. Es un
roguelite: si morís (o ganás al jefe), la run termina, gastás los puntos ganados en mejoras
permanentes y arranca una mazmorra nueva.

## Mecánicas principales

**Generación procedural de mazmorras**
- Laberinto por piso con una zona aislada (solo alcanzable por un atajo de teletransporte que hay
  que activar) y celdas "vacío" (roca sólida real, no solo una pared) que separan caminos, como en
  los mapas reales de Etrian Odyssey.
- Punto de inicio, punto final, una misión secundaria, escaleras entre pisos y salas de jefe
  obligatorias cada cierta cantidad de pisos.
- Validación automática de que el mapa completo (todos los pisos) sea 100% resoluble.

**Exploración**
- Vista en primera persona, movimiento en grilla (WASD + flechas para girar).
- Minimapa con modo "niebla de guerra" (solo lo que ya pisaste) o modo debug (mapa completo).
- Sistema de encuentros real de Etrian Odyssey: cada celda tiene un valor de peligro oculto que se
  va acumulando al caminar; al superar un límite (también oculto) aparece un encuentro y el
  contador se reinicia.
- Ítems de exploración: **Mapa** (revela todo el piso actual) y **Perforador** (abre un paso
  permanente en una pared, si hay algo real del otro lado).

**Combate por turnos**
- Party fija de 6 clases (Warrior, Protector, Ranger, Alchemist, Mage, Medic), cada una con un
  ataque básico elemental y una habilidad propia.
- Elementos al estilo Persona (Fuego, Hielo, Rayo, Corte, Golpe, Perforación) con debilidades y
  resistencias que duplican o reducen el daño.
- Orden de turnos por velocidad, Guardia, y la habilidad especial del Protector de proteger a todo
  el grupo (redirige todo el daño enemigo hacia él, a costo de TP).
- Al usar una habilidad se dispara un mini-juego de tiempo (QTE): repetir una secuencia de 3
  teclas a tiempo para pegar o curar más fuerte.
- Botones de Huir (con chance según cuántos personajes sigan vivos), Rendirse y un modo de test
  para saltar el combate.
- Enemigo Slime: al derrotarlo por primera vez se divide en dos crías más débiles.
- Escena de batalla aparte con los enemigos representados en 3D, efecto de disolución al morir o
  recibir un golpe, y un efecto de impacto animado (sprite) en los golpes de habilidad.

**Progresión entre runs (roguelite)**
- Al terminar una run (por derrota o por vencer a un jefe) se ganan puntos según el piso alcanzado
  y los enemigos derrotados.
- Esos puntos se gastan en mejoras permanentes de atributos por personaje, y en comprar cargas de
  Mapa/Perforador para la próxima run.
- El progreso se guarda en disco y sobrevive entre sesiones.

## Estado del proyecto

Prototipo en desarrollo, pensado para jugarse desde el Editor de Unity (no hay build empaquetado
todavía). La lógica de mazmorra y combate está separada en clases sin dependencia de UnityEngine,
validada con harnesses de consola aparte antes de portarla al proyecto.
