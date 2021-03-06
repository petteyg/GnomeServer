﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Game;
using GnomeServer.Extensions;
using GnomeServer.Models;
using GnomeServer.Routing;

namespace GnomeServer.Controllers
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Route("Gnome")]
    public sealed class GnomeController: ConventionRoutingController
    {
        [Route("")]
        public IResponseFormatter Get()
        {
            // Before the player has started/loaded a game, some properties won't be populated.
            var gameGnomes = GnomanEmpire.Instance.GetGnomes();
            var skillDefinitions = GnomanEmpire.Instance.GetSkillDefs();

            GnomeSummary summary = new GnomeSummary(gameGnomes, skillDefinitions);

            return JsonResponse(summary);
        }

        [Route("Add")]
        public IResponseFormatter Add()
        {
            var skillDefinitions = GnomanEmpire.Instance.GetSkillDefs();
            var faction = GnomanEmpire.Instance.World.AIDirector.PlayerFaction;
            var entryPosition = faction.FindRegionEntryPosition();

            var gnomadRaceClassDefs = faction.FactionDef.Squads.SelectMany(squad => squad.Classes.Where(squadClass => squadClass.Name == "Gnomad")).ToList();
            Int32 defCount = gnomadRaceClassDefs.Count;
            Int32 raceClassDefIndex = GnomanEmpire.Instance.Rand.Next(defCount);
            var raceClassDef = gnomadRaceClassDefs[raceClassDefIndex];
            var gnomad = new Character(entryPosition, raceClassDef, faction.ID);
            gnomad.SetBehavior(BehaviorType.PlayerCharacter);

            Dictionary<String, Int32> orderedProfessions = GetBestProfessions(gnomad);
            var bestProfession = orderedProfessions.OrderByDescending(obj => obj.Value).First().Key;
            SetProfession(gnomad, bestProfession);
            
            GnomanEmpire.Instance.EntityManager.SpawnEntityImmediate(gnomad);
            GnomanEmpire.Instance.World.NotificationManager.AddNotification(String.Format("The Gnomad {0} has arrived and been assigned as a {1}", gnomad.Name(), bestProfession));

            Gnome gnome = new Gnome(gnomad, skillDefinitions);
            return JsonResponse(gnome);
        }

        [Route("Assign")]
        public IResponseFormatter Assign()
        {
            var gnomes = GnomanEmpire.Instance.GetGnomes();
            foreach (var gnome in gnomes)
            {
                Dictionary<String, Int32> orderedProfessions = GetBestProfessions(gnome);
                var bestProfession = orderedProfessions.OrderByDescending(obj => obj.Value).First().Key;
                SetProfession(gnome, bestProfession);
                GnomanEmpire.Instance.World.NotificationManager.AddNotification(String.Format("{0} has been assigned as a {1}", gnome.Name(), bestProfession));
            }
            return Get();
        }

        private Dictionary<String, Int32> GetBestProfessions(Character gnome)
        {
            var professionSkills = GetSkillsByProfession();

            Dictionary<String, Int32> professionScores = new Dictionary<String, Int32>();
            foreach (var professionSkill in professionSkills)
            {
                var profession = professionSkill.Key;
                var skills = professionSkill.Value;

                Int32 score = 0;
                Int32 skillCount = skills.Count;
                var skillWeights = GetRatios(skillCount);
                for (Int32 i = 0; i < skillCount; i++)
                {
                    // Professions consist of multiple jobs.
                    // The other of the jobs within the profession is used when determining the next job a Gnome will perform.
                    // Note: All tasks must be completed for all higher jobs before a gnome will queue a task in a lower priority job
                    // As a result, we should weight each gnome's skills based on the likelihood of them queuing for those tasks in a given job.

                    Int32 rawSkill = gnome.SkillLevel(skills[i]);
                    Double weightedSkill = rawSkill * skillWeights[i];
                    Int32 weightedRoundedSkill = (Int32)Math.Round(weightedSkill, 0);
                    score += weightedRoundedSkill;
                }

                professionScores.Add(profession, score);
            }

            return professionScores.OrderByDescending(obj => obj.Value).ToDictionary(obj => obj.Key, obj => obj.Value);
        }

        private void SetProfession(Character gnomad, String professionTitle)
        {
            var professions = GnomanEmpire.Instance.Fortress.Professions;
            var matchingProfession = professions.SingleOrDefault(obj => obj.Title == professionTitle);
            if (matchingProfession != null)
            {
                gnomad.Mind.Profession = matchingProfession;
            }
        }

        public Dictionary<String, List<String>> GetSkillsByProfession()
        {
            var professions = GnomanEmpire.Instance.Fortress.Professions;
            Dictionary<String, List<String>> result = new Dictionary<String, List<String>>();

            foreach (var profession in professions)
            {
                var skills = profession.AllowedSkills.AllowedSkills.ToList();
                result.Add(profession.Title, skills);
            }

            return result;
        }

        /// <summary>
        /// Gets an array of coefficients that can be used to weight a numerical sequence of the specified length.
        /// The value of an element in the array represents the weighting coefficient for the item at the corresponding index in the sequence.
        /// </summary>
        /// <param name="rateOfChange">The rate at which the ratios change.</param>
        /// <param name="length">The length of the sequence for which weighting coefficients will be returned.</param>
        /// <returns>An array of <paramref name="length"/> length, that contains the weighting coefficients.</returns>
        /// <remarks>
        /// The sum of the array's elements should always equal 1.
        /// When a low value is provided for <paramref name="length"/>, the sum of the array elements is measurably smaller than 1.
        /// Consequently, after all elements in the sequence have been calculated, the remainder is divided equally between all elements such that their sum approaches 1.
        /// </remarks>
        public Double[] GetRatios(Int32 length, Double rateOfChange = 0.5)
        {
            Double[] ratios = new Double[length];
            Double currentAmount = 1;
            for (Int32 i = 0; i < ratios.Length; i++)
            {
                Double currentVal = currentAmount * rateOfChange;
                Double remaining = currentAmount - currentVal;
                currentAmount = remaining;
                ratios[i] = currentVal;
            }

            // As the number of buckets increases, the sum of the buckets approaches 1.
            // When the number of buckets is very small, a significant portion is not represented in the output.
            // To account for this, we split the remaining portion and apply it evenly among every bucket.
            currentAmount /= length;
            
            // Sometimes Resharper is a little dumb.
            // ReSharper disable once RedundantAssignment
            ratios = ratios.Select(bucket => bucket += currentAmount).ToArray();
            return ratios;
        }
    }
}
