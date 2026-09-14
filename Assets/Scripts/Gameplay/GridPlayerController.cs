using System.Collections;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    public class GridPlayerController : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public float moveDuration = 0.18f;
        public float turnDuration = 0.12f;

        private int _x, _y;
        private Direction _facing = Direction.North;
        private bool _busy;

        public int CellX => _x;
        public int CellY => _y;
        public Direction Facing => _facing;

        public void Warp(int x, int y, Direction facing)
        {
            _x = x;
            _y = y;
            _facing = facing;
            transform.position = dungeonManager.CellToWorld(_x, _y);
            transform.rotation = FacingRotation(facing);
        }

        void Update()
        {
            if (_busy || dungeonManager == null) return;

            if (Input.GetKeyDown(KeyCode.LeftArrow)) { StartCoroutine(Turn(-1)); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { StartCoroutine(Turn(1)); return; }

            Direction? moveDir = null;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) moveDir = _facing;
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) moveDir = _facing.Opposite();
            else if (Input.GetKeyDown(KeyCode.A)) moveDir = TurnDirection(_facing, -1);
            else if (Input.GetKeyDown(KeyCode.D)) moveDir = TurnDirection(_facing, 1);

            if (moveDir.HasValue)
            {
                StartCoroutine(Move(moveDir.Value));
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space)) dungeonManager.TryInteract(_x, _y);
        }

        private Direction TurnDirection(Direction facing, int steps)
        {
            int idx = ((int)facing + steps) % 4;
            if (idx < 0) idx += 4;
            return (Direction)idx;
        }

        private IEnumerator Move(Direction dir)
        {
            if (!dungeonManager.CanMove(_x, _y, dir)) yield break;

            _busy = true;
            var (ox, oy) = dir.Offset();
            int nx = _x + ox, ny = _y + oy;

            Vector3 from = transform.position;
            Vector3 to = dungeonManager.CellToWorld(nx, ny);
            float t = 0f;
            while (t < moveDuration)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(from, to, t / moveDuration);
                yield return null;
            }
            transform.position = to;
            _x = nx;
            _y = ny;

            dungeonManager.OnPlayerEnterCell(_x, _y);
            _busy = false;
        }

        private IEnumerator Turn(int steps)
        {
            _busy = true;
            _facing = TurnDirection(_facing, steps);
            Quaternion from = transform.rotation;
            Quaternion to = FacingRotation(_facing);
            float t = 0f;
            while (t < turnDuration)
            {
                t += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(from, to, t / turnDuration);
                yield return null;
            }
            transform.rotation = to;
            _busy = false;
        }

        private Quaternion FacingRotation(Direction facing)
        {
            float yaw = facing switch
            {
                Direction.North => 0f,
                Direction.East => 90f,
                Direction.South => 180f,
                Direction.West => 270f,
                _ => 0f
            };
            return Quaternion.Euler(0, yaw, 0);
        }
    }
}
