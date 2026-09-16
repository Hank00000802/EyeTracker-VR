using System.Linq;
using UnityEngine;

namespace ithappy.Creative_Characters_FREE.CharacterCustomizationTool.Editor.Randomizer.Steps.Impl
{
    public class BodyStep : IRandomizerStep
    {
        public GroupType GroupType => GroupType.Body;

        public StepResult Process(int count, GroupType[] groups)
        {
            var newGroups = groups.Where(g => g != GroupType.Body).ToArray();

            return new StepResult(Random.Range(0, count), true, newGroups);
        }
    }
}