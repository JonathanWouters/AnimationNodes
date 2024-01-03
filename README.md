# Animation Nodes
A package to help setup and control playable graphs

## How to use

#### 1. Create animation node scriptable objects
In the project tab 
Right click
Select Create/AnimationNodes

Here you can create blend1D, blend2D and state blend nodes.
Nodes can be combined with other nodes or with animation clips.

> Warning: Currently there is no check for wrong object types that get assinged in nodes

#### 2. Code
You should setup a manager script per animation graph you want to use.
Multiple animators that use the same nodetree can be added to the same graph. (recommended to make use of multithreading)

Creating a graph

    AnimationGraph = new BatchedAnimationGraph(MaxCount, RootNode);

Adding an animator to the graph.  \
The handle should be saved to later edit parameters or remove the animator from the graph.

    GraphHandle handle = AnimationGraph.CreateGraph(Animator);

Updating paramerers can be done by getting the node id of the node you want to update and providing the node handle \

    int nodeID = AnimationGraph.GetNodeIndexByName("Name"),
    
    AnimationGraph.SetStateTransition(handle, nodeID, currentstate, transitionDuration, currentTime);
    AnimationGraph.SetBlend1DParameter(handle, nodeID, float);
    AnimationGraph.SetBlend2DParameter(handle, nodeID, float2);

Evaluating the graph

    AnimationGraph.Simulate(Time.time, Time.deltaTime);
  


