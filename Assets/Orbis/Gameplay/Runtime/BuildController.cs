using System;
using System.Collections.Generic;
using Orbis.Core.Game;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;
using Orbis.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Modalità costruzione (docs/04 §4): anteprima "fantasma" dell'elemento sulla griglia del lotto puntato,
    /// verde se si può piazzare, rossa con il motivo altrimenti. Desktop: segue il mouse, clic per piazzare,
    /// R ruota, Esc annulla. Touch: sta al centro dello schermo (si sposta trascinando la mappa), ✓ per piazzare.
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        public enum Mode { None, Place, Move }

        public Mode Current { get; private set; }
        public CatalogItem Item { get; private set; }
        public string Variant { get; private set; }
        public int Rotation { get; private set; }
        public int MovingObjectId { get; private set; }
        public BuildResult Check { get; private set; } = BuildResult.LotNotLoaded;
        public long Cost { get; private set; }

        /// <summary>Su touch il punto mirato è il centro dello schermo; con il mouse il puntatore.</summary>
        public bool AimAtPointer = true;

        public event Action Changed;
        public event Action<BuildResult> Confirmed;

        private GameController _game;
        private WorldSession _world;
        private GameObject _ghost;
        private Mesh _ghostMesh;
        private Material _ghostMaterial;
        private readonly ObjectMesher _mesher = new ObjectMesher();
        private readonly MeshBuffer _mb = new MeshBuffer();
        private readonly List<PlantInstance> _plants = new List<PlantInstance>();
        private LotKey _lot;
        private int _halfX, _halfY;
        private bool _hasPosition;
        private string _lastSignature;

        public bool Active => Current != Mode.None;

        public static BuildController Attach(GameController game)
        {
            var c = game.gameObject.AddComponent<BuildController>();
            c._game = game;
            c._world = game.World;
            var shader = Resources.Load<Shader>("OrbisShaders/Highlight");
            c._ghostMaterial = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
            game.Changed += () => c._lastSignature = null; // lo stato è cambiato: si ricontrolla lo spazio
            return c;
        }

        public void StartPlacing(CatalogItem item, string variant = null)
        {
            Current = Mode.Place;
            Item = item;
            Variant = variant ?? (item.Variants.Count > 0 ? item.Variants[0].Key : null);
            Rotation = 0;
            MovingObjectId = 0;
            _lastSignature = null;
            Changed?.Invoke();
        }

        public void StartMoving(int objectId)
        {
            if (!_game.State.Objects.TryGetValue(objectId, out var o)) return;
            Current = Mode.Move;
            Item = _game.Content.Catalog.Get(o.ItemKey);
            Variant = o.Variant;
            Rotation = o.Rotation;
            MovingObjectId = objectId;
            _lastSignature = null;
            var root = _game.View.RootOf(objectId);
            if (root != null) root.SetActive(false); // si vede solo il fantasma mentre lo si sposta
            Changed?.Invoke();
        }

        public void Rotate()
        {
            if (!Active) return;
            Rotation = (Rotation + 1) & 3;
            _lastSignature = null;
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (Current == Mode.Move)
            {
                var root = _game.View.RootOf(MovingObjectId);
                if (root != null) root.SetActive(true);
            }
            Current = Mode.None;
            Item = null;
            if (_ghost != null) _ghost.SetActive(false);
            Changed?.Invoke();
        }

        public BuildResult Confirm()
        {
            if (!Active || !_hasPosition) return BuildResult.LotNotLoaded;
            if (Check != BuildResult.Ok) return Check;
            IGameCommand cmd = Current == Mode.Place
                ? new PlaceCommand { Item = Item.Key, Variant = Variant, Lot = _lot, HalfX = _halfX, HalfY = _halfY, Rotation = Rotation }
                : (IGameCommand)new MoveCommand { ObjectId = MovingObjectId, Lot = _lot, HalfX = _halfX, HalfY = _halfY, Rotation = Rotation };
            var r = _game.Do(cmd);
            Confirmed?.Invoke(r);
            if (r == BuildResult.Ok)
            {
                if (Current == Mode.Move)
                {
                    var root = _game.View.RootOf(MovingObjectId);
                    if (root != null) root.SetActive(true);
                    Cancel();
                }
                else
                {
                    // si resta in costruzione per piazzarne un altro uguale
                    _lastSignature = null;
                }
            }
            return r;
        }

        private void Update()
        {
            if (!Active) return;
            var kb = Keyboard.current;
            if (kb != null && !_world.CameraRig.KeyboardBlocked())
            {
                if (kb.rKey.wasPressedThisFrame) Rotate();
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    Cancel();
                    return;
                }
            }
            var cam = _world.CameraRig.Cam;
            Vector2 screen = AimAtPointer && Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width / 2f, Screen.height / 2f);
            var hit = _world.Picker.Raycast(cam.ScreenPointToRay(screen));
            if (!hit.Hit || hit.Tile == null)
            {
                _hasPosition = false;
                if (_ghost != null) _ghost.SetActive(false);
                return;
            }
            Aim(hit.Tile, hit.Point);
        }

        /// <summary>Calcola lotto e cella sotto il punto (metri del mondo) e aggiorna il fantasma.</summary>
        private void Aim(TileView view, Vector3 point)
        {
            var data = view.Data;
            float lx = point.x - view.Origin.x, ly = point.z - view.Origin.z;
            LotInfo lot = null;
            foreach (var l in data.Lots)
                if (l.Bounds.Contains(lx, ly) && Polygon2D.Contains(l.Outer, l.Holes, lx, ly))
                {
                    lot = l;
                    break;
                }
            if (lot == null)
            {
                _hasPosition = false;
                Check = BuildResult.OutsideLot;
                ShowGhostAt(view, lx, ly, 0f);
                return;
            }
            Placement.Extent(Item, Rotation, out var sx, out var sy);
            var o = new Vec2(lot.GridOriginX, lot.GridOriginY);
            var ax = new Vec2(Mathf.Cos(lot.GridAngle), Mathf.Sin(lot.GridAngle));
            var ay = new Vec2(-ax.Y, ax.X);
            var p = new Vec2(lx, ly) - o;
            float u = Vec2.Dot(p, ax), v = Vec2.Dot(p, ay);
            _halfX = Placement.Snap(Item, u - sx / 2f);
            _halfY = Placement.Snap(Item, v - sy / 2f);
            _lot = new LotKey(view.Id.X, view.Id.Y, lot.Id);
            _hasPosition = true;
            string sig = _lot + "|" + _halfX + "|" + _halfY + "|" + Rotation + "|" + Item.Key + "|" + _game.State.Cash / 1000;
            if (sig == _lastSignature) return;
            _lastSignature = sig;
            var ctx = _game.Session.Context;
            var s = _game.State;
            int ignore = Current == Mode.Move ? MovingObjectId : 0;
            var space = Placement.CheckSpace(ctx, Item, _lot, _halfX, _halfY, Rotation, ignore);
            if (Current == Mode.Place)
            {
                Cost = Placement.CostOf(Item, Variant);
                bool free = s.Config.CreativeMode;
                Check = !free && s.Level < Item.UnlockLevel ? BuildResult.NotUnlocked
                    : !free && s.Cash < Cost ? BuildResult.NotEnoughCash
                    : space;
            }
            else
            {
                Cost = (long)Math.Round(Placement.CostOf(Item, Variant) * ctx.Balance.MoveCostRatio);
                Check = space == BuildResult.Ok && s.Cash < Cost ? BuildResult.NotEnoughCash : space;
            }
            BuildGhost(view, lot);
            Changed?.Invoke();
        }

        private void EnsureGhost()
        {
            if (_ghost != null) return;
            _ghost = new GameObject("Anteprima");
            _ghost.transform.SetParent(_world.transform, false);
            _ghost.AddComponent<MeshFilter>();
            var mr = _ghost.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _ghostMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void BuildGhost(TileView view, LotInfo lot)
        {
            EnsureGhost();
            var f = Placement.Footprint(lot, Item, _halfX, _halfY, Rotation);
            _mb.Clear();
            _plants.Clear();
            _mesher.Build(Item, 1, Variant, new Vec2(f[0], f[1]), lot.GridAngle, Rotation, view.Data.Terrain.HeightAt, _mb, _plants, 1);
            // lastra sulla sagoma esatta: si vede sempre, anche per alberi e coperture basse
            Plate(f, view.Data.Terrain);
            foreach (var pl in _plants) PlantMarker(pl);
            if (_ghostMesh != null) Destroy(_ghostMesh);
            _ghostMesh = MeshUpload.ToMesh(_mb, "Anteprima");
            _ghost.GetComponent<MeshFilter>().sharedMesh = _ghostMesh;
            _ghost.transform.position = view.Origin;
            _ghostMaterial.SetColor("_Color", Check == BuildResult.Ok ? new Color(0.2f, 0.95f, 0.45f) : new Color(1f, 0.25f, 0.2f));
            _ghost.SetActive(true);
        }

        /// <summary>Fuori da ogni lotto (strade, piazze pubbliche): niente anteprima, solo il motivo.</summary>
        private void ShowGhostAt(TileView view, float lx, float ly, float angle)
        {
            if (_ghost != null) _ghost.SetActive(false);
            if (_lastSignature == "fuori") return;
            _lastSignature = "fuori";
            Changed?.Invoke();
        }

        private void Plate(float[] f, TerrainInfo terrain)
        {
            uint c = MeshBuffer.Rgb(0xFFFFFF);
            int start = _mb.VertexCount;
            for (int i = 0; i < 4; i++)
            {
                float x = f[2 * i], y = f[2 * i + 1];
                _mb.Add(x, y, terrain.HeightAt(x, y) + 0.3f, 0, 0, 1, c, 0, 0, 0, 0, 2, 0, 1, 0);
            }
            bool ccw = Polygon2D.SignedArea(f) > 0;
            if (ccw) _mb.Quad(start, start + 1, start + 2, start + 3);
            else _mb.Quad(start, start + 3, start + 2, start + 1);
        }

        private void PlantMarker(PlantInstance p)
        {
            // segnaposto semplice delle piante nell'anteprima: un cono alto quanto la pianta
            float h = VegetationScatter.BaseHeight(p.Kind) * p.Scale;
            float r = p.Kind == PlantKind.VineRow ? 0.4f : Mathf.Max(0.5f, h * 0.25f);
            uint c = MeshBuffer.Rgb(0xFFFFFF);
            const int sides = 6;
            int top = _mb.Add(p.X, p.Y, p.Z + h, 0, 0, 1, c, 0, 0, 0, 0, 2, 0, 1, 0);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / sides, a1 = (k + 1) * Mathf.PI * 2 / sides;
                int i0 = _mb.Add(p.X + Mathf.Cos(a0) * r, p.Y + Mathf.Sin(a0) * r, p.Z, Mathf.Cos(a0), Mathf.Sin(a0), 0.3f, c, 0, 0, 0, 0, 2, 0, 1, 0);
                int i1 = _mb.Add(p.X + Mathf.Cos(a1) * r, p.Y + Mathf.Sin(a1) * r, p.Z, Mathf.Cos(a1), Mathf.Sin(a1), 0.3f, c, 0, 0, 0, 0, 2, 0, 1, 0);
                _mb.Triangle(i0, i1, top);
            }
        }

        private void OnDestroy()
        {
            if (_ghostMesh != null) Destroy(_ghostMesh);
            if (_ghost != null) Destroy(_ghost);
        }
    }
}
