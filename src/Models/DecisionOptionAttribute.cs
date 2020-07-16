﻿namespace Landis.Extension.SOSIELHarvest.Models
{
    public class DecisionOptionAttribute
    {
        public string DecisionOption { get; set; }

        public int RequiredParticipants { get; set; }

        public string ConsequentVariable { get; set; }

        public string ConsequentValue { get; set; }

        public string ConsequentValueReference { get; set; }

        public string ConsequentValueType { get; set; }
    }
}