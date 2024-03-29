# Animation Nodes
A package to help setup and control playable graphs.
The aim is to make it as easy and fast as possible to setup playable graphs with multiple animators and provide a performant way of updating weights and time for playables.
This package uses unity.mathematics and unity.burst to calculate weight blending.

![image](https://github.com/JonathanWouters/AnimationNodes/assets/16886522/88b88d33-c433-4847-86b7-6b23e433f06f)

## Available Nodes

#### State Blend
Node to transition between different animation states.

#### Blend 1D
Node to linearly blend between animations. 

#### Blend 2D
Node to blend between animations based on an xy coordinate (freeform cartesian blend).

## How to use

#### 1. Create animation node scriptable objects
In the project tab 
Right click
Select Create/AnimationNodes

Here you can create blend1D, blend2D and state blend nodes.
Nodes can be combined with other nodes or with animation clips.

![image](https://github.com/JonathanWouters/AnimationNodes/assets/16886522/4e3f4e2c-a6ad-47a0-9b0b-cc82c97ad89d)


> Warning: Currently there is no check for wrong object types that get assinged in nodes

#### 2. Code
You should setup a manager script per animation graph you want to use.
Multiple animators that use the same nodetree can be added to the same graph. (recommended to make use of multithreading)

Creating a graph

    AnimationGraph = new BatchedAnimationGraph(MaxCount, RootNode);

Adding an animator to the graph.  \
The handle should be saved to later edit parameters or remove the animator from the graph.

    GraphHandle handle = AnimationGraph.CreateGraph(Animator);

Updating parameters can be done by getting the node id of the node you want to update and providing the node handle

    int nodeID = AnimationGraph.GetNodeIndexByName("Name"),
    
    AnimationGraph.SetStateTransition(handle, nodeID, currentstate, transitionDuration, currentTime);
    AnimationGraph.SetBlend1DParameter(handle, nodeID, float);
    AnimationGraph.SetBlend2DParameter(handle, nodeID, float2);

Evaluating the graph.  \
There are 2 optional parameters to use recursion (sometimes faster) and use multithreading (could be slower if there are not many animators attached to the graph)

    AnimationGraph.Simulate(Time.time, Time.deltaTime, useMultithreading, useRecursion);
  


