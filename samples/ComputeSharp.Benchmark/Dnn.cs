﻿using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ComputeSharp.Benchmark
{
    /// <summary>
    /// A <see langword="class"/> that contains primitives to run certain operations of a neural network
    /// </summary>
    internal static class Dnn
    {
        /// <summary>
        /// Executes the forward pass on a fully connected layer on the CPU
        /// </summary>
        /// <param name="c">The number of samples in the input tensor</param>
        /// <param name="n">The number of rows in the input matrix</param>
        /// <param name="m">The number of columns in the input matrix</param>
        /// <param name="p">The number of columns in the output matrix</param>
        /// <param name="x">The input tensor</param>
        /// <param name="w">The weights tensor</param>
        /// <param name="b">The bias tensor</param>
        /// <param name="y">The result tensor</param>
        public static void FullyConnectedForward(int c, int n, int m, int p, float[] x, float[] w, float[] b, float[] y)
        {
            void Kernel(int s)
            {
                int offset = s * n * m;
                ref float rx = ref x[s * n * m];
                ref float rw = ref w[0];
                ref float rb = ref b[0];
                ref float ry = ref y[s * n * p];

                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < p; j++)
                    {
                        float result = 0f;

                        for (int k = 0; k < m; k++)
                        {
                            result += Unsafe.Add(ref rx, i * m + k) * Unsafe.Add(ref rw, k * p + j);
                        }

                        Unsafe.Add(ref ry, i * m + j) = result + Unsafe.Add(ref rb, j);
                    }
                }
            }

            Parallel.For(0, c, Kernel);
        }
    }
}
