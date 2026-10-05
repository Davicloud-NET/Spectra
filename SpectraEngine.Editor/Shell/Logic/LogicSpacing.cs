using System;

namespace SpectraEngine.Editor.Shell.Logic;

// Pushes a stack of things apart until none touches the next.
internal static class LogicSpacing
{
    // Finds tops for a stack, from the first item down. wanted is where each
    // top would like to be, weights how hard it holds on to that (above
    // zero), steps the least distance from each top to the next. The order is
    // kept, and the tops are whole pixels when the steps are.
    public static double[] Separate(double[] wanted, double[] weights, double[] steps)
    {
        int count = wanted.Length;
        var tops = new double[count];
        if (count == 0)
            return tops;

        // With each item's room taken out, the rule left is that no value is
        // under the one before. Pooling neighbours that break it into their
        // weighted mean gives the nearest such values.
        var room = new double[count];
        for (int i = 1; i < count; i++)
            room[i] = room[i - 1] + steps[i - 1];

        var sum = new double[count];
        var weight = new double[count];
        var end = new int[count];
        int pools = 0;

        for (int i = 0; i < count; i++)
        {
            sum[pools] = weights[i] * (wanted[i] - room[i]);
            weight[pools] = weights[i];
            end[pools] = i;
            pools++;

            while (pools > 1 && sum[pools - 2] / weight[pools - 2] > sum[pools - 1] / weight[pools - 1])
            {
                sum[pools - 2] += sum[pools - 1];
                weight[pools - 2] += weight[pools - 1];
                end[pools - 2] = end[pools - 1];
                pools--;
            }
        }

        int item = 0;
        for (int pool = 0; pool < pools; pool++)
        {
            double level = Math.Round(sum[pool] / weight[pool]);
            for (; item <= end[pool]; item++)
                tops[item] = level + room[item];
        }

        return tops;
    }
}
