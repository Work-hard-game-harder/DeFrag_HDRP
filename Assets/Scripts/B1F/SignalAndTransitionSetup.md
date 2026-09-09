# B1F signal and transition setup

## Quest access and random fuel update
- Control-room closing subtitle (1) now requires `b1f_control_room`, reports `B1F_CONTROL_ROOM_REACHED` after playback, then waits for the shared acknowledgement before revealing the pending quest. Remote player colliders cannot consume a local subtitle trigger.
- Terminal_25 Download Data and Distribution Box A require `b1f_emergency_power`. Connect Server is unchanged.
- Generator B requires `b1f_full_power` and EmergencyPower; server session requests and submissions validate this.
- Assign at least three unique transforms to Generator B Controller / Fuel Spawn Points. The server spawns three registered fuel prefabs at three distinct candidates once per scene session. Empty/insufficient configuration logs an error and creates no fuel.
- The former fixed scene Fuel Tank is disabled (not deleted). The fuel prefab is already assigned and present in DefaultNetworkPrefabs.
- All three spawned cans work with the existing inventory, generator and ping; camera tracking uses the nearest uncarried can. Picking up one can stops its ping; others keep signalling.
- Fuel now requires two cans. The first can is server-consumed at 50%, its inlet session closes, and progress remains while the second can is fetched. The second can completes 100% and FullPower.
- Generator fueling is now cooperative: the inlet operator holds Space to pour while the hacking-pad operator uses the control panel and holds Space to vent. Fuel only advances between the low and danger pressure thresholds. Two seconds of overpressure emits one gameplay noise/alarm, preserves fuel progress, and pauses intake briefly.
- The server owns pressure, fuel progress, operator slots, overpressure and completion. Clients send held/released input heartbeats and render only their assigned role UI. Esc/disconnect releases that player's station.
- Verify with two players: subtitle completion from client, quest gates before/after reveal, distinct spawn positions, any of three cans accepted, and no duplicate spawn after repeated quest notifications.

## Applied defaults
- Fuel Tank model prefab: carry multiplier 0.5, signal min/max distance 8/80, linear rolloff.
- B1F generator: signal interval 6 seconds.
- Camera receiver: LocalSignalAudio is installed alongside the scanner; optional Pulse Clip overrides the generated tone. These pulses do not emit WorldNoiseSystem events.
- Terminal/generator/scanner panels use a shared teal frame and distinct command-input surface. Existing runtime UI and input behaviour remain in place.

## Cinematic hookup required
1. Stop Play mode. Select the B1F object `다음 씬으로`.
2. Add CinematicSceneTrigger (adds NetworkObject if missing). Enable BoxCollider / Is Trigger.
3. Assign the actual transition VideoClip to Cinematic. Destination Scene is B2F, now enabled in Build Settings.
4. Save B1F; use the same scene version on both peers. This is a scene NetworkObject, not a dynamic prefab registration.

The server initiates NGO scene loading while each client keeps a persistent full-screen video overlay. The overlay closes only after its video ends and its destination scene has loaded. This is loading behind a video, not deferred scene activation: B2F server simulation starts as soon as NGO activates the scene. If B2F requires delaying enemies/story timers until both viewers finish, a separate server-ready barrier must be connected to those systems.

## Play-test checklist
- Host and client can each pick up/drop fuel; carrying slows only the carrier and dropping restores speed.
- Search ping repeats at six seconds and stops on pickup. Receiver audio stops when leaving IR/view or when target search ends.
- Inspect command entry and camera clues at 16:9 and a narrower window; compare text readability against night vision.
- Test entry into the cinematic trigger from each peer; check video audio, B2F player spawning, loading longer than video, and input restoration.
- Missing clip or disabled destination refuses transition with a Console error; no placeholder broadcast is chosen automatically.

Compilation checked with dotnet; Unity visual/audio and two-player runtime verification still required.
