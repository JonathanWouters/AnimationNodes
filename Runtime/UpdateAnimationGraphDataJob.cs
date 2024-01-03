using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

using ParallelReadWrite = Unity.Collections.NativeDisableParallelForRestrictionAttribute;


namespace AnimationNodes
{
	[BurstCompile]
	internal struct UpdateAnimationGraphDataJob : IJobParallelFor
	{
		public float Time;
		public float DeltaTime;

		[ReadOnly] public NativeArray<NodeData> Nodes;
		[ReadOnly] public NativeArray<float> Positions;

		public int BufferSize;
		[ReadOnly] public NativeArray<float> DataBuffer;

		[ParallelReadWrite] public NativeArray<float> AnimationTimes;
		[ParallelReadWrite] public NativeArray<float> Weights;
		[ParallelReadWrite] public NativeArray<float> BlendedClipLenghts;
		
		public void Execute(int graphIndex)
		{
			int nodeCount = Nodes.Length;

			int startIndex = (nodeCount - 1) * graphIndex;
			NativeArray<float> weights = Weights.GetSubArray(startIndex, nodeCount-1);

			int bufferOffset = BufferSize * graphIndex;

			NativeArray<float> dataBuffer = DataBuffer.GetSubArray(bufferOffset, BufferSize);

			startIndex = nodeCount * graphIndex;
			NativeArray<float> blendedClipLenghts = BlendedClipLenghts.GetSubArray(startIndex, nodeCount);
			CalculateSingleGraph(Nodes, Positions, Time, dataBuffer, weights, blendedClipLenghts);

			AnimationTimes[graphIndex] += DeltaTime / blendedClipLenghts[0];
		}

		private static void CalculateSingleGraph(NativeArray<NodeData> nodes, NativeArray<float> positions, float time, NativeArray<float> dataBuffer, NativeArray<float> weights, NativeArray<float> blendedClipLenghts) 
		{
			int nodeCount = nodes.Length;

			// Calculate weights
			for (int i = 0; i < nodeCount; i++)
			{
				NodeData node = nodes[i];

				switch (node.Type)
				{
					case NodeData.NodeType.BlendStates:
						{
							NativeArray<float> nodeWeights = weights.GetSubArray(node.FirstChild - 1, node.ChildCount);

							int state0 = (int)dataBuffer[node.ReadPosition + 0];
							int state1 = (int)dataBuffer[node.ReadPosition + 1];

							float timeOfStateChange = dataBuffer[node.ReadPosition + 2];
							float blendDuration     = dataBuffer[node.ReadPosition + 3];

							CalculateWeightsStateBlend(state0, state1, timeOfStateChange, blendDuration, time, nodeWeights);
							break;
						}

					case NodeData.NodeType.BlendLinear:
						{
							NativeArray<float> nodePositions = positions.GetSubArray(node.FirstPosition, node.ChildCount);
							NativeArray<float> nodeWeights = weights.GetSubArray(node.FirstChild - 1, node.ChildCount);
							CalculateWeightsLinear(dataBuffer[node.ReadPosition], nodePositions, nodeWeights);
							break;
						}

					case NodeData.NodeType.BlendFreeformCartesian:
						{
							NativeArray<float2> nodePositions = positions.GetSubArray(node.FirstPosition, node.ChildCount * 2).Reinterpret<float2>(sizeof(float));
							NativeArray<float> nodeWeights = weights.GetSubArray(node.FirstChild -1, node.ChildCount);

							float2 blendPosition = new float2
							(
								dataBuffer[node.ReadPosition],
								dataBuffer[node.ReadPosition + 1]
							);

							CalculateWeightsFreeformCartesian(blendPosition, nodePositions, nodeWeights);
							break;
						}
				}
			}

			// Calculate blended clip lenghts
			for (int nodeIndex = nodeCount - 1; nodeIndex >= 0; nodeIndex--)
			{
				NodeData node = nodes[nodeIndex];

				if (node.Type == NodeData.NodeType.Clip)
				{
					blendedClipLenghts[nodeIndex] = node.Lenght;
				}
				else
				{
					float lenght = 0;
					int firstChild = node.FirstChild;
					int lastChild = firstChild + node.ChildCount;

					for (int i = firstChild; i < lastChild; i++)
						lenght += blendedClipLenghts[i] * weights[i - 1];

					blendedClipLenghts[nodeIndex] = lenght;
				}
			}
		}

		private static void CalculateWeightsStateBlend(int state0, int state1, float timeOfStateChange, float blendDuration, float time, NativeArray<float> weights)
		{
			for (int i = 0; i < weights.Length; i++)
				weights[i] = 0;

			float w1 = math.clamp((time - timeOfStateChange) / blendDuration, 0, 1);
			float w0 = 1 - w1;

			weights[state0] = w0;
			weights[state1] = w1;
		}

		private static void CalculateWeightsLinear(float blendPosition, NativeArray<float> positions, NativeArray<float> outWeights)
		{
			for (int i = 0; i < outWeights.Length; i++)
				outWeights[i] = 0;

			if (blendPosition <= positions[0])
			{
				outWeights[0] = 1;
				return;
			}

			int last = positions.Length - 1;
			if (blendPosition >= positions[last])
			{
				outWeights[last] = 1;
				return;
			}

			for (int i = 0; i < positions.Length - 1; i++)
			{
				float t0 = positions[i];
				float t1 = positions[i + 1];
				if (blendPosition >= t0 && blendPosition <= t1)
				{
					float w = math.unlerp(t0, t1, blendPosition);
					outWeights[i] = 1 - w;
					outWeights[i + 1] = w;
					return;
				}
			}
		}

		private static void CalculateWeightsFreeformCartesian(float2 blendPosition, NativeArray<float2> positions, NativeArray<float> outWeights)
		{
			float total = 0;
			for (int i = 0; i < positions.Length; i++)
			{
				float weight = float.PositiveInfinity;
				float2 directionToSamplePos = blendPosition - positions[i];

				// Calculate the impact value of the current node for each other node, and take the smallest one
				for (int j = 0; j < positions.Length; j++)
				{
					// Skip own node.
					if (i == j)
						continue;

					float2 directionToOtherNode = positions[j] - positions[i];

					// The influence value corresponds to the normalization of the projection of directionToSamplePos on directionToOtherNode.
					var h = 1 - math.dot(directionToSamplePos, directionToOtherNode) / math.lengthsq(directionToOtherNode);

					//Retain the smallest of the impact values
					weight = math.min(weight, h);

					// Debug.Assert(!float.IsNaN(weight) && !float.IsInfinity(weight), "AnimationBlendNode2D: Weight is inifinite make sure there are no clips on the same position");
				}

				weight = math.clamp(weight, 0, 1);
				total += weight;
				outWeights[i] = weight;
			}

			// Normalize all weights so the total value is 1
			for (int i = 0; i < outWeights.Length; i++)
				outWeights[i] /= total;
		}
	}
}