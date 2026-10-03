using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// The greybox playground: a park of stations for trying the fisherman's abilities and ragdoll with
    /// friends. Every peer builds the same static and clock-driven geometry here; the server spawns the
    /// networked props from PropLayout and the shared switches (PlaygroundRules, F9).
    ///
    ///   Plaza (spawn)   prop row (carry weights), box pyramid and box supply, gnome posts, reset / flop buttons
    ///   North           two ragdoll cannons (into a box wall, into a ball pit), updraft fan
    ///   East            trampolines, 12 m sky platform with a leap and an ice slide, drop towers (4.5 m, 8 m)
    ///   West            log bridge (the phase 0 gate), hammer bridge over foam, spinner arena
    ///   South-west      swinging log, merry-go-round, ice rink with tyre pucks
    ///   South           dodgeball court with ball dispensers
    /// </summary>
    public class GreyboxBuilder : MonoBehaviour
    {
        static readonly Color Ground = new Color(0.36f, 0.45f, 0.3f);
        static readonly Color Concrete = new Color(0.62f, 0.62f, 0.6f);
        static readonly Color Dark = new Color(0.22f, 0.22f, 0.25f);
        static readonly Color FoamColor = new Color(1f, 0.82f, 0.25f);
        static readonly Color Bouncy = new Color(0.9f, 0.35f, 0.65f);
        static readonly Color Ice = new Color(0.7f, 0.88f, 1f);
        static readonly Color Danger = new Color(0.9f, 0.2f, 0.15f);
        static readonly Color White = new Color(0.95f, 0.95f, 0.95f);

        PhysicsMaterial iceMaterial, bouncyMaterial, foamMaterial;
        Transform group;

        void Awake()
        {
            gameObject.AddComponent<PlaygroundClock>();
            iceMaterial = new PhysicsMaterial("Ice")
            {
                dynamicFriction = 0.02f, staticFriction = 0.02f, frictionCombine = PhysicsMaterialCombine.Minimum
            };
            bouncyMaterial = new PhysicsMaterial("Trampoline")
            {
                bounciness = 0.9f, dynamicFriction = 0.4f, staticFriction = 0.4f, bounceCombine = PhysicsMaterialCombine.Maximum
            };
            foamMaterial = new PhysicsMaterial("Foam")
            {
                bounciness = 0.3f, dynamicFriction = 0.9f, staticFriction = 0.9f, frictionCombine = PhysicsMaterialCombine.Maximum
            };

            BuildLighting();
            group = transform;
            Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(220f, 1f, 220f), Ground);

            Zone("Plaza", BuildPlaza);
            Zone("Cannons", BuildCannons);
            Zone("Updraft", () => BuildUpdraft(new Vector3(16f, 0f, 26f)));
            Zone("Trampolines", BuildTrampolinePark);
            Zone("Drop Towers", BuildDropTowers);
            Zone("Log Bridge", BuildLogBridge);
            Zone("Hammer Bridge", BuildHammerBridge);
            Zone("Spinner", () => BuildSpinner(new Vector3(-24f, 0f, 36f)));
            Zone("Swinging Log", BuildSwingFrame);
            Zone("Merry-Go-Round", () => BuildMerryGoRound(new Vector3(-36f, 0f, -16f)));
            Zone("Ice Rink", BuildIceRink);
            Zone("Dodgeball", () => BuildDodgeball(new Vector3(0f, 0f, -32f)));
            Zone("Trees", BuildTrees);

            if (FindAnyObjectByType<NetSession>() == null) new GameObject("NetSession").AddComponent<NetSession>();
        }

        void Zone(string zoneName, System.Action build)
        {
            group = new GameObject(zoneName).transform;
            group.SetParent(transform, false);
            build();
            group = transform;
        }

        void BuildLighting()
        {
            if (FindAnyObjectByType<Light>() != null) return;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // ---- plaza -----------------------------------------------------------------------------

        void BuildPlaza()
        {
            Label("ROADKILL PLAYGROUND", new Vector3(0f, 7.4f, 1f), 0.12f, FoamColor);
            Label("North: ragdoll cannons, updraft fan   ·   East: trampolines, sky platform, ice slide, drop towers\n" +
                  "West: log bridge, hammer bridge, spinner   ·   South-west: swinging log, merry-go-round, ice rink\n" +
                  "Behind you: dodgeball   ·   F9: playground rules for everyone", new Vector3(0f, 6.3f, 1f), 0.05f, White);

            Label("CARRY TEST: 1 hand lifts 20 kg, 1 player 40 kg, 2 players 80 kg", new Vector3(0f, 3f, -3f));
            Label("THROW TEST: hold G, release", new Vector3(8f, 3f, 8f));

            Plate("RESET PROPS", new Vector3(-7f, 0f, -11f), PlaygroundRules.Action.ResetProps, new Color(0.2f, 0.6f, 0.95f));
            Plate("EVERYBODY FLOP!", new Vector3(7f, 0f, -11f), PlaygroundRules.Action.FlopEveryone, Danger);

            Dispenser("Box Supply", new Vector3(3f, 1.4f, 8f), "Box5", 5, 2f, new Vector3(0.4f, 0f, 0.4f));
            Label("BOX SUPPLY", new Vector3(3f, 2.6f, 8f));

            foreach (float x in PropLayout.GnomePostsX)
                Block("Gnome Post", new Vector3(x, PropLayout.GnomePostHeight * 0.5f, PropLayout.GnomePostsZ),
                    new Vector3(0.3f, PropLayout.GnomePostHeight, 0.3f), PropLibrary.Wood);
            Label("KNOCK THE GNOMES OFF", new Vector3(14f, 3f, PropLayout.GnomePostsZ));
        }

        void Plate(string text, Vector3 position, PlaygroundRules.Action action, Color color)
        {
            var root = new GameObject($"Plate: {text}").transform;
            root.SetParent(group, false);
            root.position = position;
            var baseBlock = Block("Plate Base", position + Vector3.up * 0.03f, new Vector3(2f, 0.06f, 2f), Dark);
            baseBlock.transform.SetParent(root, true);
            var cap = Cylinder("Button", position + Vector3.up * 0.14f, 0.7f, 0.16f, color, ColliderKind.None);
            cap.transform.SetParent(root, true);
            var plate = root.gameObject.AddComponent<PressurePlate>();
            plate.action = action;
            plate.size = new Vector3(2f, 1f, 2f);
            plate.cap = cap.transform;
            Label($"{text}\n(stand on it)", position + Vector3.up * 1.8f);
        }

        void Dispenser(string dispenserName, Vector3 position, string prefab, int count, float interval, Vector3 spread)
        {
            var go = new GameObject(dispenserName);
            go.transform.SetParent(group, false);
            go.transform.position = position;
            var dispenser = go.AddComponent<PropDispenser>();
            dispenser.prefabName = prefab;
            dispenser.count = count;
            dispenser.interval = interval;
            dispenser.spread = spread;
        }

        // ---- north: cannons, ball pit, updraft ----------------------------------------------------

        void BuildCannons()
        {
            Cannon("CANNON → BOX WALL", new Vector3(-5f, 0f, 18f), PropLayout.BoxWall + new Vector3(0f, 0f, 2.5f));   // through the wall, into the foam
            Foam("Box Wall Catch", PropLayout.BoxWall + new Vector3(0f, 0f, 6f), new Vector3(9f, 0.3f, 8f));

            Vector3 pit = new Vector3(7f, 0f, 42f);
            Cannon("CANNON → BALL PIT", new Vector3(5f, 0f, 18f), pit);
            BuildBallPit(pit, 7f);
        }

        void Cannon(string text, Vector3 position, Vector3 target)
        {
            Vector3 toward = Vector3.ProjectOnPlane(target - position, Vector3.up).normalized;
            Quaternion facing = Quaternion.LookRotation(toward, Vector3.up);

            var pad = Cylinder("Cannon Pad", position + Vector3.up * 0.02f, 1.2f, 0.04f, Danger, ColliderKind.None);
            pad.transform.SetParent(group, true);
            var trigger = new GameObject("Cannon Trigger").AddComponent<BoxCollider>();
            trigger.transform.SetParent(group, false);
            trigger.transform.SetPositionAndRotation(position + Vector3.up * 1f, facing);
            trigger.size = new Vector3(2f, 2f, 2f);
            trigger.isTrigger = true;

            // The barrel leans over the pad toward the target.
            Block("Cannon Mount", position - toward * 2.2f + Vector3.up * 0.5f, new Vector3(1.6f, 1f, 1.6f), Dark, facing);
            var barrel = Cylinder("Cannon Barrel", position - toward * 1.6f + Vector3.up * 1.6f, 0.6f, 3f, Dark, ColliderKind.None);
            barrel.transform.rotation = facing * Quaternion.Euler(50f, 0f, 0f);

            var countdown = Label("", position + Vector3.up * 3.2f, 0.14f, Danger);
            var cannon = trigger.gameObject.AddComponent<RagdollCannon>();
            cannon.target = target;
            cannon.apexHeight = 6f;
            cannon.countdown = countdown;
            cannon.barrel = barrel.transform;
            Label($"{text}\nstand on the red pad", position + Vector3.up * 4.4f);
        }

        void BuildBallPit(Vector3 center, float inner)
        {
            const float wall = 0.6f, thickness = 0.3f;
            float half = inner * 0.5f + thickness * 0.5f;
            Color rim = new Color(0.3f, 0.5f, 0.9f);
            Block("Pit Wall", center + new Vector3(0f, wall * 0.5f, half), new Vector3(inner + thickness * 2f, wall, thickness), rim);
            Block("Pit Wall", center + new Vector3(0f, wall * 0.5f, -half), new Vector3(inner + thickness * 2f, wall, thickness), rim);
            Block("Pit Wall", center + new Vector3(half, wall * 0.5f, 0f), new Vector3(thickness, wall, inner), rim);
            Block("Pit Wall", center + new Vector3(-half, wall * 0.5f, 0f), new Vector3(thickness, wall, inner), rim);
            Foam("Pit Floor", center, new Vector3(inner, 0.1f, inner));

            // Local clutter: each peer has its own balls (they never affect who gets hit).
            Color[] colors = { Danger, FoamColor, new Color(0.25f, 0.6f, 0.95f), new Color(0.35f, 0.8f, 0.3f), Bouncy };
            var ballMaterial = new PhysicsMaterial("Pit Ball") { bounciness = 0.4f, dynamicFriction = 0.3f, staticFriction = 0.3f };
            var rng = new System.Random(7);
            var balls = new GameObject("Balls").transform;
            balls.SetParent(group, false);
            for (int i = 0; i < 260; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Pit Ball";
                ball.transform.SetParent(balls, false);
                float x = ((float)rng.NextDouble() - 0.5f) * (inner - 0.6f);
                float z = ((float)rng.NextDouble() - 0.5f) * (inner - 0.6f);
                ball.transform.position = center + new Vector3(x, 0.35f + (i / 60) * 0.45f, z);
                ball.transform.localScale = Vector3.one * 0.44f;
                ball.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(colors[i % colors.Length]);
                ball.GetComponent<Collider>().sharedMaterial = ballMaterial;
                var body = ball.AddComponent<Rigidbody>();
                body.mass = 0.4f;
                body.linearDamping = 0.3f;
                body.angularDamping = 0.5f;
            }
            Label("BALL PIT", center + Vector3.up * 2.5f);
        }

        void BuildUpdraft(Vector3 position)
        {
            // Everything around the fan is foam: drifting out of the column is a soft landing.
            Foam("Fan Foam", position, new Vector3(11f, 0.3f, 11f));
            position += Vector3.up * 0.3f;
            Cylinder("Fan Housing", position + Vector3.up * 0.05f, 1.7f, 0.1f, Dark, ColliderKind.Static);
            var blades = new GameObject("Fan Blades").transform;
            blades.SetParent(group, false);
            blades.position = position + Vector3.up * 0.15f;
            for (int i = 0; i < 2; i++)
            {
                var blade = Block("Blade", blades.position, new Vector3(3f, 0.05f, 0.35f), White, Quaternion.Euler(0f, i * 90f, 0f));
                Object.Destroy(blade.GetComponent<Collider>());
                blade.transform.SetParent(blades, true);
            }
            var column = new GameObject("Updraft").AddComponent<BoxCollider>();
            column.transform.SetParent(group, false);
            column.transform.position = position + Vector3.up * 6.1f;
            column.size = new Vector3(3.2f, 12f, 3.2f);
            column.isTrigger = true;
            column.gameObject.AddComponent<Updraft>().blades = blades;
            Label("UPDRAFT FAN\nwalk in, float up", position + Vector3.up * 2.4f);
        }

        // ---- east: trampolines, sky platform, slide, drop towers ---------------------------------

        void BuildTrampolinePark()
        {
            Trampoline("BOUNCE", new Vector3(18f, 0f, -17f), 8f);
            Trampoline("BIG BOUNCE", new Vector3(24f, 0f, -17f), 12f);
            Trampoline("SUPER BOUNCE\nhold W at the top to land on the sky platform", new Vector3(30f, 0f, -17f), 17f);

            // The sky platform: leap into the foam (west) or onto concrete, or take the ice slide down (east).
            const float top = 12f;
            Vector3 platform = new Vector3(30f, 0f, -11.5f);
            Block("Sky Platform", platform + Vector3.up * (top * 0.5f), new Vector3(5f, top, 5f), Concrete);
            Label("SKY PLATFORM 12 m\nfoam to the west, ice slide to the east", platform + Vector3.up * (top + 2f));
            Foam("Leap Foam", new Vector3(22f, 0f, -8f), new Vector3(8f, 0.3f, 7f));
            Label("LEAP OF FAITH", new Vector3(22f, 2.5f, -8f));

            // Ice slide down to an ice run-out; whoever is still fast at the foam tumbles into it.
            Vector3 slideTop = new Vector3(32.5f, top, -11.5f);
            var slide = Ramp(slideTop, new Vector3(52f, 0f, -11.5f), 3f, Ice);
            Slippery(slide, 0.03f);
            slide.GetComponent<PlaygroundSurface>().softLanding = true;
            Rails(slide, 3f);
            var runout = Block("Ice Run-out", new Vector3(57f, 0.02f, -11.5f), new Vector3(10f, 0.04f, 3f), Ice);
            Slippery(runout, 0.03f);
            runout.GetComponent<PlaygroundSurface>().softLanding = true;
            Foam("Slide Landing", new Vector3(68f, 0f, -11.5f), new Vector3(12f, 0.1f, 9f));
            Label("ICE SLIDE", slideTop + new Vector3(2f, 2f, 0f));
        }

        void Trampoline(string text, Vector3 position, float bounce)
        {
            var bed = Block("Trampoline", position + Vector3.up * 0.2f, new Vector3(3.6f, 0.4f, 3.6f), Bouncy);
            bed.GetComponent<Collider>().sharedMaterial = bouncyMaterial;
            bed.AddComponent<PlaygroundSurface>().bounceSpeed = bounce;
            var frame = Block("Trampoline Frame", position + Vector3.up * 0.15f, new Vector3(4f, 0.3f, 4f), Dark);
            Object.Destroy(frame.GetComponent<Collider>());
            Label(text, position + Vector3.up * 2.6f);
        }

        void BuildDropTowers()
        {
            Tower("4.5 m: ragdoll, no damage", new Vector3(20f, 0f, 10f), 4.5f);
            Tower("8 m: ragdoll and 20 damage", new Vector3(32f, 0f, 14f), 8f);
        }

        void Tower(string text, Vector3 baseCenter, float height)
        {
            const float size = 4f;
            Block("Tower", baseCenter + Vector3.up * (height * 0.5f), new Vector3(size, height, size), Concrete);
            float front = baseCenter.z - size * 0.5f;
            float run = height / Mathf.Tan(25f * Mathf.Deg2Rad);
            Ramp(new Vector3(baseCenter.x, 0f, front - run), new Vector3(baseCenter.x, height, front), 2.5f, Concrete);
            Label(text, baseCenter + Vector3.up * (height + 1.5f));
        }

        // ---- west: bridges, spinner ---------------------------------------------------------------

        void BuildLogBridge()
        {
            const float x = -20f, deck = 1.5f;
            Block("Bridge Deck A", new Vector3(x, deck * 0.5f, 0f), new Vector3(4f, deck, 4f), Concrete);
            Block("Bridge Deck B", new Vector3(x, deck * 0.5f, 12f), new Vector3(4f, deck, 4f), Concrete);
            Ramp(new Vector3(x, 0f, -2f - 3.5f), new Vector3(x, deck, -2f), 2.5f, Concrete);
            Ramp(new Vector3(x, 0f, 14f + 3.5f), new Vector3(x, deck, 14f), 2.5f, Concrete);

            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "Log";
            log.transform.SetParent(group, false);
            log.transform.SetPositionAndRotation(new Vector3(x, deck - 0.3f, 6f), Quaternion.Euler(90f, 0f, 0f));
            log.transform.localScale = new Vector3(0.6f, 4f, 0.6f);
            log.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(PropLibrary.Wood);

            Label("PHASE 0 GATE: two players carry the 75 kg crate across the log", new Vector3(x, deck + 3f, 6f));
        }

        void BuildHammerBridge()
        {
            const float x = -34f, deck = 3f, beamStart = 2f, beamEnd = 16f;
            Block("Hammer Deck A", new Vector3(x, deck * 0.5f, 0f), new Vector3(4f, deck, 4f), Concrete);
            Block("Hammer Deck B", new Vector3(x, deck * 0.5f, 18f), new Vector3(4f, deck, 4f), Concrete);
            float run = deck / Mathf.Tan(25f * Mathf.Deg2Rad);
            Ramp(new Vector3(x, 0f, -2f - run), new Vector3(x, deck, -2f), 2.5f, Concrete);
            Ramp(new Vector3(x, 0f, 20f + run), new Vector3(x, deck, 20f), 2.5f, Concrete);
            Block("Hammer Beam", new Vector3(x, deck - 0.2f, (beamStart + beamEnd) * 0.5f), new Vector3(1f, 0.4f, beamEnd - beamStart + 0.2f), PropLibrary.Wood);
            Foam("Hammer Foam", new Vector3(x, 0f, 9f), new Vector3(8f, 0.3f, beamEnd - beamStart));

            // Overhead gantry the hammers hang from.
            const float bar = 8.4f;
            foreach (float z in new[] { 3f, 15f })
            {
                Block("Gantry Post", new Vector3(x - 3.8f, bar * 0.5f, z), new Vector3(0.3f, bar, 0.3f), Dark);
                Block("Gantry Post", new Vector3(x + 3.8f, bar * 0.5f, z), new Vector3(0.3f, bar, 0.3f), Dark);
                Block("Gantry Beam", new Vector3(x, bar, z), new Vector3(7.9f, 0.3f, 0.3f), Dark);
            }
            Block("Gantry Spine", new Vector3(x, bar, 9f), new Vector3(0.3f, 0.3f, 12.3f), Dark);

            float[] hammerZ = { 5.5f, 9f, 12.5f };
            for (int i = 0; i < hammerZ.Length; i++)
            {
                var pivot = new Vector3(x, bar - 0.2f, hammerZ[i]);
                var root = new GameObject("Hammer");
                root.transform.SetParent(group, false);
                root.transform.position = pivot;
                root.AddComponent<Rigidbody>();
                var arm = Block("Hammer Arm", pivot + Vector3.down * 2.3f, new Vector3(0.2f, 4.4f, 0.2f), PropLibrary.Wood);
                arm.transform.SetParent(root.transform, true);
                var head = Block("Hammer Head", pivot + Vector3.down * 4.6f, new Vector3(1.6f, 1.2f, 1f), PropLibrary.HunterOrange);
                head.transform.SetParent(root.transform, true);
                var mover = root.AddComponent<KinematicMover>();
                mover.mode = KinematicMover.Mode.Swing;
                mover.axis = Vector3.forward;
                mover.pivot = pivot;
                mover.amplitude = 65f;
                mover.period = 2.8f;
                mover.phase = i / 3f;
                var hazard = root.AddComponent<Hazard>();
                hazard.upKick = 2.5f;
            }
            Label("HAMMER BRIDGE: cross without getting smacked", new Vector3(x, bar + 1.5f, 9f));
        }

        void BuildSpinner(Vector3 center)
        {
            const float radius = 9f;
            Cylinder("Spinner Floor", center + Vector3.up * 0.02f, radius, 0.04f, new Color(0.45f, 0.4f, 0.55f), ColliderKind.Static);
            Cylinder("Spinner Hub", center + Vector3.up * 1.2f, 0.6f, 2.4f, Dark, ColliderKind.Static);

            var rotor = new GameObject("Spinner Rotor");
            rotor.transform.SetParent(group, false);
            rotor.transform.position = center;
            rotor.AddComponent<Rigidbody>();
            float length = radius - 0.6f;
            var low = Block("Low Arm", center + new Vector3(0.6f + length * 0.5f, 0.35f, 0f), new Vector3(length, 0.35f, 0.35f), PropLibrary.HunterOrange);
            low.transform.SetParent(rotor.transform, true);
            var high = Block("High Arm", center + new Vector3(-0.6f - length * 0.5f, 1.6f, 0f), new Vector3(length, 0.3f, 0.3f), Danger);
            high.transform.SetParent(rotor.transform, true);
            var mover = rotor.AddComponent<KinematicMover>();
            mover.mode = KinematicMover.Mode.Spin;
            mover.axis = Vector3.up;
            mover.pivot = center;
            mover.speed = 110f;
            mover.speedCycleSeconds = 24f;
            rotor.AddComponent<Hazard>();
            Label("SPINNER: jump the low arm, crouch (Ctrl) under the high one\nlast one standing wins", center + Vector3.up * 4f);
        }

        // ---- south-west: swinging log, merry-go-round, ice rink -----------------------------------

        void BuildSwingFrame()
        {
            Vector3 pivot = PropLayout.SwingPivot;
            Block("Swing Post L", new Vector3(pivot.x - 2f, 3f, pivot.z), new Vector3(0.3f, 6f, 0.3f), PropLibrary.Wood);
            Block("Swing Post R", new Vector3(pivot.x + 2f, 3f, pivot.z), new Vector3(0.3f, 6f, 0.3f), PropLibrary.Wood);
            Block("Swing Beam", pivot, new Vector3(4.3f, 0.3f, 0.3f), PropLibrary.Wood);
            Label("STRUCK TEST: 60 kg log, stand in its path", pivot + Vector3.up * 1.5f);
        }

        void BuildMerryGoRound(Vector3 center)
        {
            const float radius = 4.5f;
            var root = new GameObject("Merry-Go-Round");
            root.transform.SetParent(group, false);
            root.transform.position = center;
            root.AddComponent<Rigidbody>();
            var disc = Cylinder("Disc", center + Vector3.up * 0.08f, radius, 0.16f, new Color(0.95f, 0.6f, 0.2f), ColliderKind.Convex);
            disc.transform.SetParent(root.transform, true);
            var pole = Cylinder("Pole", center + Vector3.up * 1.1f, 0.2f, 2.2f, White, ColliderKind.Convex);
            pole.transform.SetParent(root.transform, true);
            for (int i = 0; i < 4; i++)
            {
                Vector3 at = center + Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 3.3f;
                var post = Block("Handle", at + Vector3.up * 0.75f, new Vector3(0.15f, 1.2f, 0.15f), Danger);
                post.transform.SetParent(root.transform, true);
            }
            var mover = root.AddComponent<KinematicMover>();
            mover.mode = KinematicMover.Mode.Spin;
            mover.axis = Vector3.up;
            mover.pivot = center;
            mover.speed = 200f;
            mover.speedCycleSeconds = 16f;
            root.AddComponent<PlaygroundSurface>().grip = 0.7f;
            Label("MERRY-GO-ROUND: hold on... you can't", center + Vector3.up * 3.6f);
        }

        void BuildIceRink()
        {
            Vector3 center = PropLayout.IceRink;
            var rink = Block("Ice Rink", center + Vector3.up * 0.05f, new Vector3(20f, 0.1f, 14f), Ice);
            Slippery(rink, 0.06f);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 mouth = center + new Vector3(side * 9f, 0.1f, 0f);
                Block("Goal Post", mouth + new Vector3(0f, 0.7f, 1.5f), new Vector3(0.15f, 1.4f, 0.15f), Danger);
                Block("Goal Post", mouth + new Vector3(0f, 0.7f, -1.5f), new Vector3(0.15f, 1.4f, 0.15f), Danger);
                Block("Goal Bar", mouth + new Vector3(0f, 1.4f, 0f), new Vector3(0.15f, 0.15f, 3.15f), Danger);
                Block("Goal Back", mouth + new Vector3(side * 0.8f, 0.6f, 0f), new Vector3(0.1f, 1.2f, 3f), White);
            }
            Label("ICE RINK: kick the tyres into the goals", center + Vector3.up * 3f);
        }

        // ---- south: dodgeball ------------------------------------------------------------------------

        void BuildDodgeball(Vector3 center)
        {
            var floor = Block("Court", center + Vector3.up * 0.005f, new Vector3(18f, 0.01f, 10f), new Color(0.3f, 0.45f, 0.7f));
            Object.Destroy(floor.GetComponent<Collider>());
            Line(center, new Vector3(18f, 0.02f, 0.12f));
            Line(center + new Vector3(0f, 0f, 5f), new Vector3(18f, 0.02f, 0.12f));
            Line(center + new Vector3(0f, 0f, -5f), new Vector3(18f, 0.02f, 0.12f));
            Line(center + new Vector3(9f, 0f, 0f), new Vector3(0.12f, 0.02f, 10f));
            Line(center + new Vector3(-9f, 0f, 0f), new Vector3(0.12f, 0.02f, 10f));
            Dispenser("Dodgeballs North", center + new Vector3(0f, 2.5f, 2.5f), "Dodgeball", 4, 1.5f, new Vector3(3f, 0f, 0.5f));
            Dispenser("Dodgeballs South", center + new Vector3(0f, 2.5f, -2.5f), "Dodgeball", 4, 1.5f, new Vector3(3f, 0f, 0.5f));
            Label("DODGEBALL: a good throw knocks them flat", center + Vector3.up * 3.6f);
        }

        void Line(Vector3 center, Vector3 size)
        {
            var line = Block("Court Line", center + Vector3.up * 0.012f, size, White);
            Object.Destroy(line.GetComponent<Collider>());
        }

        void BuildTrees()
        {
            var rng = new System.Random(13);
            for (int i = 0; i < 60; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float radius = 80f + (float)rng.NextDouble() * 22f;
                Vector3 p = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Block("Trunk", p + Vector3.up * 3f, new Vector3(0.6f, 6f, 0.6f), PropLibrary.Wood);
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Crown";
                crown.transform.SetParent(group, false);
                crown.transform.position = p + Vector3.up * 7f;
                crown.transform.localScale = Vector3.one * 4f;
                crown.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(new Color(0.18f, 0.42f, 0.2f));
            }
        }

        // ---- helpers ---------------------------------------------------------------------------

        enum ColliderKind { None, Static, Convex }

        GameObject Block(string blockName, Vector3 center, Vector3 size, Color color, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = blockName;
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(color);
            return go;
        }

        /// <summary>An upright cylinder. The primitive's capsule collider would make a flat disc a ball, so use the mesh.</summary>
        GameObject Cylinder(string cylinderName, Vector3 center, float radius, float height, Color color, ColliderKind kind)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = cylinderName;
            go.transform.SetParent(group, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = RkMaterials.Get(color);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (kind != ColliderKind.None)
            {
                var mesh = go.AddComponent<MeshCollider>();
                mesh.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                mesh.convex = kind == ColliderKind.Convex;
            }
            return go;
        }

        GameObject Ramp(Vector3 bottom, Vector3 top, float width, Color color)
        {
            const float thickness = 0.3f;
            Vector3 along = top - bottom;
            Quaternion rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            Vector3 center = (bottom + top) * 0.5f - rotation * Vector3.up * (thickness * 0.5f);
            return Block("Ramp", center, new Vector3(width, thickness, along.magnitude + 0.2f), color, rotation);
        }

        /// <summary>Low walls along both sides of a ramp so sliders stay on it.</summary>
        void Rails(GameObject ramp, float width)
        {
            Transform t = ramp.transform;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 center = t.position + t.right * side * (width * 0.5f + 0.1f) + t.up * 0.4f;
                Block("Rail", center, new Vector3(0.2f, 0.8f, t.localScale.z), Concrete, t.rotation);
            }
        }

        void Slippery(GameObject surface, float grip)
        {
            surface.GetComponent<Collider>().sharedMaterial = iceMaterial;
            surface.AddComponent<PlaygroundSurface>().grip = grip;
        }

        void Foam(string foamName, Vector3 groundCenter, Vector3 size)
        {
            var foam = Block(foamName, groundCenter + Vector3.up * (size.y * 0.5f), size, FoamColor);
            foam.GetComponent<Collider>().sharedMaterial = foamMaterial;
            foam.AddComponent<PlaygroundSurface>().softLanding = true;
        }

        TextMesh Label(string text, Vector3 position, float characterSize = 0.06f, Color? color = null)
        {
            var go = new GameObject($"Label: {text.Split('\n')[0]}");
            go.transform.SetParent(group, false);
            go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;
            mesh.fontSize = 48;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color ?? Color.white;
            go.AddComponent<FaceCamera>();
            return mesh;
        }
    }

    /// <summary>Turns a world label toward the active camera.</summary>
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
