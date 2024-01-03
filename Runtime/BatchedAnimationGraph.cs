using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;
using Unity.Profiling;
using System;

namespace AnimationNodes
{
	[BurstCompile]
	public class BatchedAnimationGraph : IDisposable
	{
		private PlayableGraph _graph;
		private NodeGraph _nodeGraph;

		private NativeArray<AnimationPlayableOutput> _outputs;
		private NativeArray<Playable> _playables;
		private NativeArray<float> _dataBuffer;
		private NativeArray<float> _weights;
		private NativeArray<float> _blendedClipLenghts;
		private NativeArray<float> _animationTimes;

		private int _activeGraphCount = 0;
		private Stack<int> _freeIDs;
		private int[] _graphLookup;

		internal static class Markers
		{
			public static readonly ProfilerMarker UpdateGraphs = new ProfilerMarker(nameof(UpdateGraphs));
			public static readonly ProfilerMarker SetWeightsAndTime = new ProfilerMarker(nameof(SetWeightsAndTime));
			public static readonly ProfilerMarker EvaluateGraph = new ProfilerMarker(nameof(EvaluateGraph));
		}

		public BatchedAnimationGraph(int maxGraphCount, Object rootNode)
		{
			_nodeGraph = NodeGraph.CreateFromAnimationNode(rootNode);
			int nodeCount = _nodeGraph.Nodes.Length;

			_graph              = PlayableGraph.Create("Graph");
			
			_weights            = new NativeArray<float>(maxGraphCount * (nodeCount - 1)      , Allocator.Persistent);
			_dataBuffer         = new NativeArray<float>(maxGraphCount * _nodeGraph.BufferSize, Allocator.Persistent);
			_blendedClipLenghts = new NativeArray<float>(maxGraphCount * nodeCount            , Allocator.Persistent);
			_animationTimes     = new NativeArray<float>(maxGraphCount, Allocator.Persistent);
			_outputs            = new NativeArray<AnimationPlayableOutput>(maxGraphCount, Allocator.Persistent);
			_playables          = new NativeArray<Playable>(maxGraphCount * nodeCount         , Allocator.Persistent);

			_freeIDs = new Stack<int>(maxGraphCount);
			for (int i = maxGraphCount - 1; i >= 0; i--)
				_freeIDs.Push(i);

			_graphLookup = new int[maxGraphCount];
			for (int i = 0; i < maxGraphCount; i++)
				_graphLookup[i] = -1;
		}

		public void Dispose()
		{
			_nodeGraph.Dispose();
			_weights.Dispose();
			_dataBuffer.Dispose();
			_blendedClipLenghts.Dispose();
			_animationTimes.Dispose();
			_playables.Dispose();
			_outputs.Dispose();
			_graph.Destroy();
		}

		public int GetNodeIndexByName(string name) 
		{
			for (int i = 0; i < _nodeGraph.Names.Length; i++)
			{
				if (_nodeGraph.Names[i] == name)
					return i;
			}

			Debug.LogError("Name not found.");
			return -1;
		}

		public GraphHandle CreateGraph(Animator animator) 
		{
			int nodeCount = _nodeGraph.Nodes.Length;
			var output = CreatePlayables(_graph, animator, _nodeGraph, _playables.GetSubArray(_activeGraphCount * nodeCount, nodeCount));

			int id = _freeIDs.Pop();
			_outputs[id] = output;
			_graphLookup[id] = _activeGraphCount;

			_activeGraphCount++;

			return new GraphHandle { Index = id };
		}
		
		public void RemoveGraph(GraphHandle handle) 
		{
			Debug.Assert(handle.IsValid);

			int id = handle;
			Debug.Assert(_freeIDs.Contains(id) == false);

			if (_graph.IsValid() == false)
				return;

			_activeGraphCount--;
			_freeIDs.Push(id);
			int indexToRemove = _graphLookup[id];
			int indexOfLast = _activeGraphCount;

			_graph.DestroyOutput(_outputs[id]);
			_graphLookup[id] = handle;

			// Update the index (ptr) of the last graph to reflect the swap. 
			for (int i = 0; i < _graphLookup.Length; i++)
			{
				if (_graphLookup[i] == indexOfLast)
				{
					_graphLookup[i] = indexToRemove;
					continue;
				}
			}

			// Swap weights with last graph
			{
				int weigthCount = _nodeGraph.Nodes.Length - 1;
				NativeArray<float> weights = _weights.GetSubArray(indexToRemove * weigthCount, weigthCount);
				NativeArray<float> weightsLast = _weights.GetSubArray(indexOfLast * weigthCount, weigthCount);
				weights.CopyFrom(weightsLast);

				for (int i = 0; i < weigthCount; i++)
					weightsLast[i] = 0;
			}

			// Swap time with last graph
			{
				_animationTimes[indexToRemove] = _animationTimes[indexOfLast];
			}

			// Swap playables
			{
				int nodeCounts = _nodeGraph.Nodes.Length;
				NativeArray<Playable> playables = _playables.GetSubArray(indexToRemove * nodeCounts, nodeCounts);
				NativeArray<Playable> playablesLast = _playables.GetSubArray(indexOfLast * nodeCounts, nodeCounts);
				playables.CopyFrom(playablesLast);


				for (int i = 0; i < nodeCounts; i++)
					playablesLast[i] = new Playable();
			}

			// Swap data buffer
			{
				NativeArray<float> buffer = _dataBuffer.GetSubArray(indexToRemove * _nodeGraph.BufferSize, _nodeGraph.BufferSize);
				NativeArray<float> bufferLast = _dataBuffer.GetSubArray(indexOfLast * _nodeGraph.BufferSize, _nodeGraph.BufferSize);
				buffer.CopyFrom(bufferLast);

				for (int i = 0; i < _nodeGraph.BufferSize; i++)
					bufferLast[i] = 0;
			}

		}

		private static AnimationPlayableOutput CreatePlayables(PlayableGraph graph, Animator animator, NodeGraph tree, NativeArray<Playable> OutPlayables)
		{
			var playableOutput = AnimationPlayableOutput.Create(graph, "Output", animator);

			// Reverse loop because parents rely on the value of their children.
			for (int i = tree.Nodes.Length - 1; i >= 0; i--)
			{
				NodeData node = tree.Nodes[i];
				NodeData.NodeType type = node.Type;
				int metaDataIndex = tree.MetaDataIndices[i];

				if (type == NodeData.NodeType.Clip)
				{
					AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, tree.Clips[metaDataIndex]);
					OutPlayables[i] = playable;
				}
				else
				{
					NativeSlice<Playable> children = OutPlayables.Slice(node.FirstChild, node.ChildCount);

					AnimationMixerPlayable mixer = AnimationMixerPlayable.Create(graph, children.Length);
					for (int childIndex = 0; childIndex < children.Length; childIndex++)
						graph.Connect(children[childIndex], 0, mixer, childIndex);

					OutPlayables[i] = mixer;
				}
			}

			playableOutput.SetSourcePlayable(OutPlayables[0]);
			return playableOutput;
		}

		public void SetStateTransition(GraphHandle graphHandle, int nodeIndex, int newState, float transitionTime, float currentTime) 
		{
			NodeData node =  _nodeGraph.Nodes[nodeIndex];

			Debug.Assert(node.Type == NodeData.NodeType.BlendStates);

			int graphIndex = _graphLookup[graphHandle];
			int position = node.ReadPosition + _nodeGraph.BufferSize * graphIndex;

			if (_dataBuffer[position + 1] == newState)
				return;

			_dataBuffer[position + 0] = _dataBuffer[position + 1];
			_dataBuffer[position + 1] = newState;
			_dataBuffer[position + 2] = currentTime;
			_dataBuffer[position + 3] = transitionTime;
		}

		public void SetBlend1DParameter(GraphHandle graphHandle, int nodeIndex, float param) 
		{
			NodeData node = _nodeGraph.Nodes[nodeIndex];
			Debug.Assert(node.Type == NodeData.NodeType.BlendLinear);

			int graphIndex = _graphLookup[graphHandle];
			int position = node.ReadPosition + _nodeGraph.BufferSize * graphIndex;
			_dataBuffer[position] = param;
		}

		public void SetBlend2DParameter(GraphHandle graphHandle, int nodeIndex, float2 param)
		{
			NodeData node = _nodeGraph.Nodes[nodeIndex];
			Debug.Assert(node.Type == NodeData.NodeType.BlendFreeformCartesian);

			int graphIndex = _graphLookup[graphHandle];
			int position = node.ReadPosition + _nodeGraph.BufferSize * graphIndex;
			_dataBuffer[position + 0] = param.x;
			_dataBuffer[position + 1] = param.y;
		}

		public void Simulate(float time, float deltaTime, bool multithread = false, bool recursive = false)
		{
			Markers.UpdateGraphs.Begin();
			UpdateAnimationGraphDataJob calculateWeightsJob = new UpdateAnimationGraphDataJob()
			{
				Nodes              = _nodeGraph.Nodes,
				Positions          = _nodeGraph.Positions,
				Weights            = _weights,
				Time               = time,
				DeltaTime          = deltaTime,
				BufferSize         = _nodeGraph.BufferSize,
				DataBuffer         = _dataBuffer,
				BlendedClipLenghts = _blendedClipLenghts,
				AnimationTimes     = _animationTimes,
			};

			if (multithread)
			{
				JobHandle handle = calculateWeightsJob.Schedule(_activeGraphCount, 1);
				handle.Complete();
			}
			else
			{
				calculateWeightsJob.Run(_activeGraphCount);
			}

			Markers.SetWeightsAndTime.Begin();

			if (recursive)
			{
				UpdatePlayablesRecursively(_activeGraphCount, _nodeGraph.Nodes.Length, ref _animationTimes,
					ref _blendedClipLenghts, ref _weights, ref _nodeGraph.Nodes, ref _playables);
			}
			else
			{
				UpdatePlayables(_activeGraphCount, _nodeGraph.Nodes.Length, ref _animationTimes, 
					ref _blendedClipLenghts, ref _weights, ref _nodeGraph.Nodes, ref _playables);
			}

			Markers.SetWeightsAndTime.End();

			Markers.UpdateGraphs.End();

			Markers.EvaluateGraph.Begin();
			_graph.Evaluate();
			Markers.EvaluateGraph.End();
		}

		[BurstCompile]
		internal static void UpdatePlayables(int graphCount, int nodeCount, ref NativeArray<float> animationTimes, 
			ref NativeArray<float> blendedClipLenghts, ref NativeArray<float> weights, ref NativeArray<NodeData> Nodes, ref NativeArray<Playable> playables) 
		{
			for (int graphIndex = 0; graphIndex < graphCount; graphIndex++)
			{
				float animationTime = animationTimes[graphIndex];
				int nodeOffset = nodeCount * graphIndex;
				int weightOffset = (nodeCount - 1) * graphIndex;

				for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
				{
					if (nodeIndex != 0 && weights[weightOffset + nodeIndex - 1] == 0)
						continue;

					int index = nodeIndex + nodeOffset;
					NodeData node = Nodes[nodeIndex];

					playables[index].SetTime(animationTime * blendedClipLenghts[index]);

					for (int childIndex = 0; childIndex < node.ChildCount; childIndex++)
						playables[index].SetInputWeight(childIndex, weights[weightOffset + node.FirstChild + childIndex - 1]);
				}
			}
		}

		[BurstCompile]
		internal static void UpdatePlayablesRecursively(int graphCount, int nodeCount, ref NativeArray<float> animationTimes,
			ref NativeArray<float> blendedClipLenghts, ref NativeArray<float> weights, ref NativeArray<NodeData> Nodes, ref NativeArray<Playable> playables)
		{
			for (int graphIndex = 0; graphIndex < graphCount; graphIndex++)
			{
				float animationTime = animationTimes[graphIndex];
				int nodeOffset = nodeCount * graphIndex;
				int weightOffset = (nodeCount - 1) * graphIndex;

				void SetWeightAndTimeRecursively(
					int nodeIndex,
					ref NativeArray<float> animationTimes,
					ref NativeArray<float> blendedClipLenghts,
					ref NativeArray<float> weights,
					ref NativeArray<NodeData> nodes,
					ref NativeArray<Playable> playables)
				{
					NodeData node = nodes[nodeIndex];
					playables[nodeIndex + nodeOffset].SetTime(animationTime * blendedClipLenghts[nodeIndex + nodeOffset]);

					for (int childIndex = 0; childIndex < node.ChildCount; childIndex++)
						playables[nodeIndex + nodeOffset].SetInputWeight(childIndex, weights[weightOffset + node.FirstChild - 1 + childIndex]);

					for (int childIndex = 0; childIndex < node.ChildCount; childIndex++)
					{
						int next = node.FirstChild + childIndex;
						if (weights[weightOffset + next - 1] == 0)
							continue;

						SetWeightAndTimeRecursively(next, ref animationTimes, ref blendedClipLenghts, ref weights, ref nodes, ref playables);
					}
				}

				SetWeightAndTimeRecursively(0, ref animationTimes, ref blendedClipLenghts, ref weights, ref Nodes, ref playables);
			}
		}

	}
}