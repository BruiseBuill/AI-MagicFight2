using System.Collections.Generic;
using MagicBrawl.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MagicBrawl.App
{
    /// <summary>Presentation-only card travel, matched by UID across successive snapshots.</summary>
    [DisallowMultipleComponent]
    public sealed class CardTransitView : MonoBehaviour
    {
        private struct Pose
        {
            public Vector3 Center;
            public Vector2 Size;
            public Quaternion Rotation;
        }

        private sealed class Flight
        {
            public int Uid;

            /// <summary>
            /// 这张牌属于哪一侧（M35）。<b>起点与落点都靠它定位</b> ——
            /// 冷却区有两片（玩家 / 怪物），「这张牌的槽在哪」必须按座位查，不能写死玩家侧。
            /// </summary>
            public int Seat;

            public bool Returning;
            public RectTransform Ghost;
            public Pose From;
            public float Elapsed;
            public float Delay;
        }

        [Header("构件（场景预建，构建器接线）")]
        [Tooltip("飞牌用的自由层（铺满画布、不吃射线）。")]
        [SerializeField] private RectTransform _layer;

        [Tooltip("一张「飞行中的牌」的模板（失活 Image）—— 运行时从它实例化，用完回收复用。")]
        [SerializeField] private Image _flightTemplate;

        private HandView _hand;
        private CooldownView _cooldown;

        /// <summary>回收的飞行卡（避免每张牌都走一次 Instantiate / Destroy）。</summary>
        private readonly List<Image> _ghostPool = new List<Image>();

        private readonly Dictionary<int, Pose> _handOrigins = new Dictionary<int, Pose>();

        /// <summary>
        /// 冷却区里的牌「此刻占屏的矩形」—— <b>按座位分开</b>（下标 = 座位，见 <see cref="Flight.Seat"/>）。
        ///
        /// <para>⚠ M35 之前这里只有一份：于是怪物打出的牌进冷却区时，
        /// <c>Find</c> 去玩家的冷却区里找、找不到，那趟飞行被静默跳过 ——
        /// 表现是「只有玩家的牌会飞」。现在两片冷却区各有各的表。</para>
        /// </summary>
        private readonly Dictionary<int, Pose>[] _coolingOrigins =
        {
            new Dictionary<int, Pose>(), new Dictionary<int, Pose>(),
        };

        /// <summary>
        /// 「角色头顶正展示着」那批牌的起始位姿（M35）—— 键 = uid。
        ///
        /// <para>由 <c>BattleUi</c> 在刷新时灌入：某张牌刚在快照里出现在冷却区、而它此刻还挂在
        /// 角色头顶（<c>PlayedCardView</c> 里），就把那块矩形登记进来，这趟飞行便<b>从头顶起飞</b>。
        /// 这就替掉了旧观感 ——「头顶那张凭空消失、冷却槽里凭空冒出来一张」。</para>
        ///
        /// <para><b>优先级高于手牌起点</b>：刚打出的牌从来不在手牌快照里（引擎在发事件之前
        /// 就把它挪出 <c>Hand</c> 了），所以两者实际不会同时命中；真撞上时以「头顶」为准，
        /// 因为它才是玩家最后看到这张牌的位置。</para>
        /// </summary>
        private readonly Dictionary<int, Pose> _playedOrigins = new Dictionary<int, Pose>();

        private readonly HashSet<int> _previousHand = new HashSet<int>();

        /// <summary>上一趟刷新时各侧冷却区里的 uid（下标 = 座位）——「新面孔」靠它判。</summary>
        private readonly HashSet<int>[] _previousCooling =
        {
            new HashSet<int>(), new HashSet<int>(),
        };

        private readonly List<Flight> _flights = new List<Flight>();
        private readonly Vector3[] _corners = new Vector3[4];

        /// <summary>
        /// 接上手牌区与冷却区（幂等）。
        ///
        /// <para><b>层与模板都在场景里预建</b>（<c>BattleCanvas/CardTransitLayer</c>，
        /// 由 M8 构建器建出来）：它们要能在 Hierarchy 里被看见、被调
        /// （颜色 / 尺寸 / 叠放顺序），所以不运行时 new。这里只绑两个引用。</para>
        /// </summary>
        public void Configure(HandView hand, CooldownView cooldown)
        {
            _hand = hand;
            _cooldown = cooldown;
        }

        /// <summary>取一张飞行卡：优先复用池里的，池空才从模板实例化。</summary>
        private Image TakeGhost()
        {
            if (_flightTemplate == null)
            {
                return null;
            }

            if (_ghostPool.Count > 0)
            {
                int last = _ghostPool.Count - 1;
                Image reused = _ghostPool[last];
                _ghostPool.RemoveAt(last);
                reused.gameObject.SetActive(true);
                return reused;
            }

            Image ghost = Instantiate(_flightTemplate, _layer);
            ghost.gameObject.SetActive(true);
            return ghost;
        }

        private void ReleaseGhost(RectTransform ghost)
        {
            if (ghost == null)
            {
                return;
            }

            var img = ghost.GetComponent<Image>();
            if (img == null)
            {
                return;
            }

            ghost.SetParent(_layer, false);
            ghost.gameObject.SetActive(false);
            _ghostPool.Add(img);
        }

        public void CaptureBeforeBind(IReadOnlyList<CardSnapshot> hand,
            IReadOnlyList<CardSnapshot> playerCooling, IReadOnlyList<CardSnapshot> enemyCooling)
        {
            if (_layer == null) return;
            Capture(hand, true, BattleState.SeatPlayer, _previousHand, _handOrigins);
            Capture(playerCooling, false, BattleState.SeatPlayer,
                _previousCooling[BattleState.SeatPlayer], _coolingOrigins[BattleState.SeatPlayer]);
            Capture(enemyCooling, false, BattleState.SeatAi,
                _previousCooling[BattleState.SeatAi], _coolingOrigins[BattleState.SeatAi]);
        }

        /// <summary>
        /// 登记「某张牌此刻正挂在角色头顶」的位姿（M35）。
        ///
        /// <para>调用时机：刷新时发现这张牌的 uid 已经在冷却区快照里（= 引擎第 ④ 步把它送进去了），
        /// 而它在上一帧还挂在头顶 —— 这时把头顶那块矩形登记进来，随后的
        /// <see cref="AnimateAfterBind"/> 就会让飞行卡从这里起飞。</para>
        ///
        /// <para>幂等：同一张牌在同一位置重复登记没有副作用（每趟刷新都会重登一次，
        /// 因为飞行真正开始前它的位置一直是同一个）。</para>
        /// </summary>
        public void PrimePlayedOrigin(int uid, RectTransform rect)
        {
            if (_layer == null || rect == null)
            {
                return;
            }

            _playedOrigins[uid] = ReadPose(rect);
        }

        private void Capture(IReadOnlyList<CardSnapshot> cards, bool hand, int seat,
            HashSet<int> previous, Dictionary<int, Pose> origins)
        {
            previous.Clear();
            for (int i = 0; i < cards.Count; i++)
            {
                int uid = cards[i].Uid;
                previous.Add(uid);
                CardView view = Find(uid, hand, seat);
                if (view == null) continue;
                RectTransform rect = (RectTransform)view.transform;
                for (int f = 0; f < _flights.Count; f++)
                    if (_flights[f].Uid == uid) { rect = _flights[f].Ghost; break; }
                origins[uid] = hand ? ReadPose(rect) : ReadCoolingPose(rect);
            }
        }

        public void AnimateAfterBind(IReadOnlyList<CardSnapshot> hand,
            IReadOnlyList<CardSnapshot> playerCooling, IReadOnlyList<CardSnapshot> enemyCooling)
        {
            if (_hand == null || _cooldown == null || _layer == null) return;

            // ① 冷却区 → 手牌（只有玩家的牌可能回手：敌方手牌对玩家不可见，不进 HandView）
            int returning = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                if (_previousHand.Contains(hand[i].Uid))
                {
                    continue;
                }

                int fromSeat = FindCoolingSeat(hand[i].Uid);
                if (fromSeat < 0)
                {
                    continue;
                }

                Pose from = _coolingOrigins[fromSeat][hand[i].Uid];
                _coolingOrigins[fromSeat].Remove(hand[i].Uid);
                Launch(hand[i], from, true, returning++ * 0.07f, BattleState.SeatPlayer);
            }

            // ② 手牌 / 头顶 → 冷却区（两侧各走一趟）
            DepartCooling(playerCooling, BattleState.SeatPlayer);
            DepartCooling(enemyCooling, BattleState.SeatAi);

            // Snapshot refreshes must not reveal the destination before the moving face arrives.
            for (int i = 0; i < _flights.Count; i++)
            {
                CardView target = Find(_flights[i].Uid, _flights[i].Returning, _flights[i].Seat);
                if (target != null) target.SetPresentationHidden(true);
            }
        }

        /// <summary>
        /// 某一侧冷却区里「这一趟新出现」的牌 → 起飞。
        ///
        /// <para>起点优先级：<b>头顶</b>（刚打出的牌）＞ <b>手牌</b>（效果直接送入冷却，如雷云）。
        /// 两者都没有（比如开局就把牌塞进冷却）就不加动画，直接出现。</para>
        /// </summary>
        private void DepartCooling(IReadOnlyList<CardSnapshot> cooling, int seat)
        {
            HashSet<int> previous = _previousCooling[seat];
            Dictionary<int, Pose> origins = _coolingOrigins[seat];

            for (int i = 0; i < cooling.Count; i++)
            {
                if (previous.Contains(cooling[i].Uid))
                {
                    continue;
                }

                Pose from;

                // M35：刚从头顶撤下来的牌优先从「头顶」起飞 —— 那才是玩家最后看到它的位置。
                //（旧观感是头顶那张凭空消失、冷却槽里凭空冒出来一张，两个瞬变之间没有联系。）
                if (_playedOrigins.TryGetValue(cooling[i].Uid, out from))
                {
                    _playedOrigins.Remove(cooling[i].Uid);
                    _handOrigins.Remove(cooling[i].Uid);
                    Launch(cooling[i], from, false, 0f, seat);
                    continue;
                }

                if (origins.TryGetValue(cooling[i].Uid, out from))
                {
                    Launch(cooling[i], from, false, 0f, seat);
                    origins.Remove(cooling[i].Uid);
                }
            }
        }

        /// <summary>这张牌此刻挂在哪一侧的冷却区里（<c>-1</c> = 都没有）。</summary>
        private int FindCoolingSeat(int uid)
        {
            for (int seat = 0; seat < _coolingOrigins.Length; seat++)
            {
                if (_coolingOrigins[seat].ContainsKey(uid))
                {
                    return seat;
                }
            }

            return -1;
        }

        private void Launch(CardSnapshot card, Pose from, bool returning, float delay, int seat)
        {
            for (int i = _flights.Count - 1; i >= 0; i--)
                if (_flights[i].Uid == card.Uid) Finish(i);
            CardView target = Find(card.Uid, returning, seat);
            if (target == null) return;
            Image face = TakeGhost();
            if (face == null) return;
            RectTransform rect = (RectTransform)face.transform;
            rect.name = "CardFlight_" + card.Uid;
            var library = CardArtLibrary.Instance;
            face.sprite = library == null ? null : library.GetArt(card.CardId);
            face.preserveAspect = true;
            face.raycastTarget = false;
            ApplyPose(rect, from);
            target.SetPresentationHidden(true);
            _flights.Add(new Flight
            {
                Uid = card.Uid, Seat = seat, Returning = returning, Ghost = rect, From = from, Delay = delay,
            });
        }

        private void LateUpdate()
        {
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                Flight flight = _flights[i];
                CardView target = Find(flight.Uid, flight.Returning, flight.Seat);
                if (target == null) { Finish(i); continue; }
                flight.Elapsed += Time.unscaledDeltaTime;
                float elapsed = flight.Elapsed - flight.Delay;
                if (elapsed < 0f) continue;
                Pose to = flight.Returning ? ReadPose((RectTransform)target.transform)
                    : ReadCoolingPose((RectTransform)target.transform);
                Pose pose = flight.From;
                const float prepare = 0.16f;
                const float travel = 0.46f;
                Vector2 preparedSize = flight.Returning ? flight.From.Size * 1.25f : flight.From.Size * 0.72f;
                if (elapsed < prepare)
                {
                    float t = Mathf.SmoothStep(0f, 1f, elapsed / prepare);
                    pose.Size = Vector2.Lerp(flight.From.Size, preparedSize, t);
                    pose.Center += Vector3.up * (flight.Returning ? 18f : 10f) * t;
                    pose.Rotation = Quaternion.Slerp(flight.From.Rotation, Quaternion.identity, t);
                }
                else
                {
                    float t = Mathf.Clamp01((elapsed - prepare) / travel);
                    float eased = Mathf.SmoothStep(0f, 1f, t);
                    Vector3 start = flight.From.Center + Vector3.up * (flight.Returning ? 18f : 10f);
                    pose.Center = Vector3.Lerp(start, to.Center, eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 65f);
                    pose.Size = Vector2.Lerp(preparedSize, to.Size, eased);
                    pose.Rotation = Quaternion.Slerp(Quaternion.identity, to.Rotation, eased);
                }
                ApplyPose(flight.Ghost, pose);
                if (elapsed >= prepare + travel) Finish(i);
            }
        }

        private CardView Find(int uid, bool hand, int seat)
        {
            return hand ? (_hand == null ? null : _hand.FindCard(uid))
                : (_cooldown == null ? null : _cooldown.FindCard(seat, uid));
        }

        private Pose ReadPose(RectTransform rect)
        {
            rect.GetWorldCorners(_corners);
            Vector3 bottomLeft = _layer.InverseTransformPoint(_corners[0]);
            Vector3 topLeft = _layer.InverseTransformPoint(_corners[1]);
            Vector3 topRight = _layer.InverseTransformPoint(_corners[2]);
            return new Pose
            {
                Center = (bottomLeft + topRight) * 0.5f,
                Size = new Vector2(Vector3.Distance(topLeft, topRight), Vector3.Distance(bottomLeft, topLeft)),
                Rotation = Quaternion.Inverse(_layer.rotation) * rect.rotation
            };
        }

        private Pose ReadCoolingPose(RectTransform rect)
        {
            Pose pose = ReadPose(rect);
            // Scrolled-out rows use the nearest visible cooling edge for both arrival and departure.
            var scroll = rect.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.viewport != null)
            {
                scroll.viewport.GetWorldCorners(_corners);
                float bottom = _layer.InverseTransformPoint(_corners[0]).y;
                float top = _layer.InverseTransformPoint(_corners[1]).y;
                pose.Center.y = Mathf.Clamp(pose.Center.y,
                    bottom + pose.Size.y * 0.5f, top - pose.Size.y * 0.5f);
            }
            return pose;
        }

        private static void ApplyPose(RectTransform rect, Pose pose)
        {
            rect.localPosition = pose.Center;
            rect.sizeDelta = pose.Size;
            rect.localRotation = pose.Rotation;
        }

        private void Finish(int index)
        {
            Flight flight = _flights[index];
            CardView target = Find(flight.Uid, flight.Returning, flight.Seat);
            if (target != null) target.SetPresentationHidden(false);
            ReleaseGhost(flight.Ghost);
            _flights.RemoveAt(index);
        }

        public void ResetPresentation()
        {
            for (int i = _flights.Count - 1; i >= 0; i--) Finish(i);
            _handOrigins.Clear();
            _playedOrigins.Clear();
            _previousHand.Clear();

            for (int seat = 0; seat < _coolingOrigins.Length; seat++)
            {
                _coolingOrigins[seat].Clear();
                _previousCooling[seat].Clear();
            }
        }

        private void OnDisable() { ResetPresentation(); }
    }
}
