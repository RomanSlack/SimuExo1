# SimuVerse Unity Frontend

This is the Unity frontend for the SimuVerse agent simulation system. It handles the visual representation, physics, and environment interactions for the agents.

## Setup

1. Make sure you have the Unity Universal Render Pipeline (URP) installed.

2. Add the following tags to your Unity project (Edit → Project Settings → Tags and Layers):
   - `Agent` - For identifying agent game objects
   - `Building` - For identifying buildings
   - `Interactable` - For interactable objects
   - `Item` - For pickupable items
   - `Door` - For doors and entrances
   - `Resource` - For resources

3. Add an `Agent` layer to your project layers list.

4. Configure the WorldManager component:
   - Set `Spawn Center Position` to your desired spawn location
   - Set `Spawn Y Position` to the correct height for your terrain
   - Configure `Initial Agent Count` to set how many agents to spawn

5. Make sure the Python backend is running before starting the simulation.

## Controls

- `Shift + X`: Manually trigger a simulation cycle
- First Person Controller: WASD to move, mouse to look

## Agent Detection System

The EnvironmentReporter component handles detecting nearby agents and objects:

- `Agent Detection Radius`: How far agents can detect other agents (default: 30 units)
- `Object Detection Radius`: How far agents can detect objects (default: 15 units)
- `Field Of View Angle`: Agent's field of view (default: 120 degrees)
- `Require Line Of Sight`: If true, agents need unobstructed view to detect others

## Creating Custom Interactable Objects

1. Add a GameObject to your scene
2. Add the `InteractableObject` component
3. Set the `Description` field to describe the object
4. Tag it with one of the defined tags (e.g., `Interactable`, `Item`)

## Agent Profiles

Agent personalities and tasks are configured on the Python backend in the `agent_profiles.json` file. See the backend README for details on how to configure them.

## Troubleshooting

- If agents don't spawn correctly, check that your NavMesh is properly baked
- If agents don't detect each other, verify they have the `Agent` tag and layer
- If the environment isn't updating, check console logs for connection issues with the backend
- If agents get stuck, check for NavMesh obstacles or unreachable areas

## Communication with Backend

The Unity frontend communicates with the Python backend through HTTP APIs:

- Unity sends regular environment updates to the backend
- The backend sends action commands back to Unity
- All communication uses JSON format