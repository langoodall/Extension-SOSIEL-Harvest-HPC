// Copyright (C) 2021 SOSIEL Inc. All rights reserved.
// Use of this source code is governed by a license that can be found
// in the LICENSE file located in the repository root directory.

using System.Collections.Generic;

namespace Landis.Extension.SOSIELHarvest.Models
{
    public class AgentToManagementArea
    {
        public AgentToManagementArea()
        {
            ManagementAreas = new List<string>();
        }

        public string Agent { get; set; }
        public List<string> ManagementAreas { get; }

        public SiteSelectionMethod SiteSelectionMethod { get; set; }
    }
}
