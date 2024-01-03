using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;
using System;


namespace AnimationNodes
{
	internal struct NodeGraph : IDisposable
	{
		internal NativeArray<NodeData> Nodes;
		internal NativeArray<FixedString128Bytes> Names;
		internal NativeArray<int> MetaDataIndices;
		internal List<AnimationClip> Clips;
		internal NativeArray<float> Positions;
		internal int BufferSize;

		public void Dispose()
		{
			Nodes.Dispose();
			Names.Dispose();
			MetaDataIndices.Dispose();
			Positions.Dispose();
		}

		public static NodeGraph CreateFromAnimationNode(Object root)
		{
			NativeList<NodeData> nodes = new NativeList<NodeData>(Allocator.Temp);
			NativeList<float> positions = new NativeList<float>(Allocator.Temp);
			NativeList<FixedString128Bytes> names = new NativeList<FixedString128Bytes>(Allocator.Temp);
			NativeList<int> metaDataIndices = new NativeList<int>(Allocator.Temp);
			List<AnimationClip> clips = new List<AnimationClip>();

			int readIndex = 0;
			Queue<Object> queue = new Queue<Object>();
			queue.Enqueue(root);
			while (queue.Count != 0)
			{
				var node = queue.Dequeue();
				names.Add(node.name);
				if (node is AnimationClip clip)
				{
					nodes.Add(new NodeData()
					{
						Type = NodeData.NodeType.Clip,
						Lenght = clip.length,
					});

					metaDataIndices.Add(clips.Count);
					clips.Add(clip);
				}
				else if (node is Blend1DAnimationNode blend1D)
				{
					int childCount = blend1D.Nodes.Length;

					nodes.Add(new NodeData()
					{
						Type = NodeData.NodeType.BlendLinear,

						ChildCount = childCount,
						FirstChild = nodes.Length + 1 + queue.Count,
						FirstPosition = positions.Length,
						ReadPosition = readIndex,
					});

					readIndex++;
					metaDataIndices.Add(-1);

					for (int i = 0; i < blend1D.Nodes.Length; i++)
						positions.Add(blend1D.Nodes[i].Position);

					foreach (var child in blend1D.Nodes)
						queue.Enqueue(child.Node);

				}
				else if (node is Blend2DAnimationNode blend2D)
				{
					int childCount = blend2D.Nodes.Length;

					nodes.Add(new NodeData()
					{
						Type = NodeData.NodeType.BlendFreeformCartesian,

						ChildCount = childCount,
						FirstChild = nodes.Length + 1 + queue.Count,
						FirstPosition = positions.Length,
						ReadPosition = readIndex,
					});

					readIndex += 2;
					metaDataIndices.Add(-1);

					for (int i = 0; i < blend2D.Nodes.Length; i++)
					{
						positions.Add(blend2D.Nodes[i].Position.x);
						positions.Add(blend2D.Nodes[i].Position.y);
					}

					foreach (var child in blend2D.Nodes)
						queue.Enqueue(child.Node);
				}
				else if (node is StateBlendAnimationNode state)
				{
					int childCount = state.Nodes.Length;

					nodes.Add(new NodeData()
					{
						Type = NodeData.NodeType.BlendStates,

						ChildCount = childCount,
						FirstChild = nodes.Length + 1 + queue.Count,
						FirstPosition = 0,
					});

					readIndex += 4;
					metaDataIndices.Add(-1);

					foreach (var child in state.Nodes)
						queue.Enqueue(child);
				}
				else
				{
					Debug.LogError($"{node.name} is of an invalid Type {node.GetType()}", node);
				}
			}

			var result = new NodeGraph
			{
				Nodes = new NativeArray<NodeData>(nodes.AsArray(), Allocator.Persistent),
				Positions = new NativeArray<float>(positions.AsArray(), Allocator.Persistent),
				Names = new NativeArray<FixedString128Bytes>(names.AsArray(), Allocator.Persistent),
				MetaDataIndices = new NativeArray<int>(metaDataIndices.AsArray(), Allocator.Persistent),
				Clips = clips,
				BufferSize = readIndex
			};

			metaDataIndices.Dispose();
			nodes.Dispose();
			positions.Dispose();
			return result;
		}
	}
}