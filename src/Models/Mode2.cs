// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (C) 2021 SOSIEL Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Landis.Extension.SOSIELHarvest.Algorithm;
using Landis.Extension.SOSIELHarvest.Helpers;
using Landis.Library.HarvestManagement;
using Landis.Utilities;

using HarvestManagement = Landis.Library.HarvestManagement;

namespace Landis.Extension.SOSIELHarvest.Models
{
    public class Mode2 : Mode
    {
        private readonly BiomassHarvest.PlugIn _biomassHarvest;
        private readonly List<ExtendedPrescription> _extendedPrescriptions;
        private static readonly Regex _decisionPattern = new Regex(@"(MM\d+-\d+_DO\d+)");

        public Mode2(PlugIn plugin)
            : base(2, plugin)
        {
            _biomassHarvest = plugin.BiomassHarvest;
            _extendedPrescriptions = new List<ExtendedPrescription>();
        }

        protected override void InitializeMode()
        {
            _biomassHarvest.Initialize();

            var biomassHarvestPluginType = _biomassHarvest.GetType();
            var managementAreasField = biomassHarvestPluginType.GetField("managementAreas",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (managementAreasField == null)
                throw new Exception("Can't get management area list from BHE");

            Areas = ((IManagementAreaDataset) managementAreasField.GetValue(_biomassHarvest)).ToDictionary(
                area => area.MapCode.ToString(),
                managementArea =>
                {
                    var area = new Area();
                    area.Initialize(managementArea);
                    return area;
                });

            foreach (var biomassHarvestArea in Areas.Values)
            {
                foreach (var appliedPrescription in biomassHarvestArea.ManagementArea.Prescriptions)
                {
                    _extendedPrescriptions.Add(
                        appliedPrescription.ToExtendedPrescription(biomassHarvestArea.ManagementArea));
                }
            }

            foreach (var agentToManagementArea in sheParameters.AgentToManagementAreaList)
            {
                foreach (var managementAreaName in agentToManagementArea.ManagementAreas)
                {
                    var area = Areas[managementAreaName];
                    area.AssignedAgents.Add(agentToManagementArea.Agent);
                }
            }
        }

        protected override void Harvest()
        {
            log.WriteLine("Run Mode2 harvesting ...");

            foreach (var doModel in sosielData.NewDecisionOptions)
            {
                GenerateNewPrescription(doModel);
            }

            foreach (var selectedDecision in sosielData.SelectedDecisions)
            {
                var managementArea = Areas[selectedDecision.Key].ManagementArea;
                managementArea.Prescriptions.RemoveAll(p => _decisionPattern.IsMatch(p.Prescription.Name));
                foreach (var selectedDesignName in selectedDecision.Value)
                {
                    var extendedPrescription =
                        _extendedPrescriptions.FirstOrDefault(ep =>
                            ep.ManagementArea.MapCode.Equals(managementArea.MapCode) &&
                            ep.Name.Equals(selectedDesignName));
                    if (extendedPrescription != null)
                        ApplyPrescription(managementArea, extendedPrescription);
                }
            }

            _biomassHarvest.Run();
        }

        private const double kEpsilon = 0.0001;

        protected override HarvestResults AnalyzeHarvestingResult()
        {
            var results = new HarvestResults();
            foreach (var managementArea in Areas.Values.Select(a => a.ManagementArea))
            {
                var key = managementArea.MapCode.ToString();
                results.ManageAreaBiomass[key] = 0.0;
                results.ManageAreaHarvested[key] = 0.0;
                results.ManageAreaMaturityPercent[key] = 0.0;

                double manageAreaMaturityProportion = 0.0;
                foreach (var stand in managementArea)
                {
                    double standMaturityProportion = 0.0;
                    foreach (var site in stand)
                    {
                        double siteBiomass = 0;
                        double siteMaturity = 0;
                        foreach (var species in PlugIn.ModelCore.Species)
                        {
                            var cohorts = BiomassHarvest.SiteVars.Cohorts[site][species];
                            if (cohorts != null)
                            {
                                double siteSpeciesMaturity = 0.0;
                                foreach (var cohort in cohorts)
                                {
                                    siteBiomass += cohort.Biomass;
                                    if (cohort.Age >= PlugIn.ModelCore.Species[species.Name].Maturity)
                                        siteSpeciesMaturity += cohort.Biomass;
                                }
                                siteMaturity += siteSpeciesMaturity;
                            }
                        }
                        var siteMaturityProportion = Math.Abs(siteBiomass) < kEpsilon
                            ? 0.0 : (siteMaturity / siteBiomass);
                        standMaturityProportion += siteMaturityProportion;
                        results.ManageAreaBiomass[key] += siteBiomass;
                        results.ManageAreaHarvested[key] += BiomassHarvest.SiteVars.BiomassRemoved[site];
                    }
                    manageAreaMaturityProportion += standMaturityProportion / stand.Count();
                }

                manageAreaMaturityProportion /= managementArea.StandCount;
                results.ManageAreaBiomass[key] = (results.ManageAreaBiomass[key] / 100) * PlugIn.ModelCore.CellArea;
                results.ManageAreaHarvested[key] = (results.ManageAreaHarvested[key] / 100) * PlugIn.ModelCore.CellArea;
                results.ManageAreaMaturityPercent[key] = 100 * manageAreaMaturityProportion;
            }
            return results;
        }

        private void GenerateNewPrescription(NewDecisionOptionModel doModel)
        {
            // Filter out parameters that we do not want to handle
            switch (doModel.ConsequentVariable)
            {
                // Add more known parameters here
                case "PercentOfHarvestArea":
                    break;

                default:
                {
                    log.WriteLine($"Mode 2: GenerateNewPrescription: Skipping parameter {doModel.ConsequentVariable}");
                    return;
                }
            }

            var managementArea = Areas[doModel.ManagementArea].ManagementArea;
            var appliedPrescription = managementArea.Prescriptions.FirstOrDefault(
                p => p.Prescription.Name.Equals(doModel.BasedOn));
            if (appliedPrescription == null) return;
            var areaToHarvest = appliedPrescription.PercentageToHarvest;
            var standsToHarvest = appliedPrescription.PercentStandsToHarvest;
            var beginTime = appliedPrescription.BeginTime;
            var endTime = appliedPrescription.EndTime;

            HarvestManagement.Prescription newPrescription;
            switch (doModel.ConsequentVariable)
            {
                case "PercentOfHarvestArea":
                {
                    var newAreaToHarvest = new Percentage(doModel.ConsequentValue / 100);
                    double cuttingMultiplier =
                        areaToHarvest.Value > 0 ? newAreaToHarvest.Value / areaToHarvest.Value : 1;
                    areaToHarvest = newAreaToHarvest;
                    newPrescription = appliedPrescription.Prescription.Copy(doModel.Name, cuttingMultiplier);
                    break;
                }
                default: throw new Exception($"Invalid parameter {doModel.ConsequentVariable}");
            }

            _extendedPrescriptions.Add(new ExtendedPrescription(
                newPrescription, managementArea, areaToHarvest, standsToHarvest, beginTime, endTime));
        }

        private void ApplyPrescription(ManagementArea managementArea, ExtendedPrescription extendedPrescription)
        {
            managementArea.ApplyPrescription(extendedPrescription.Prescription,
                new Percentage(extendedPrescription.HarvestAreaPercent),
                new Percentage(extendedPrescription.HarvestStandsAreaPercent), extendedPrescription.StartTime,
                    extendedPrescription.EndTime);
            managementArea.FinishInitialization();
        }
    }
}
