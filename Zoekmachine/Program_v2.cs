using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using Newtonsoft.Json;

namespace Zoekmachine.v2 {
    public class NestedResponseDTO {
        private Random rnd = new();
        private string _naam;
        private string _naamvoorspelbaar;
        private TestResponseDTO _testresponsedto;

        public NestedResponseDTO(TestResponseDTO testresponsedto=null) {
            _naam = "NestedNaam" + rnd.Next(int.MinValue, int.MaxValue).ToString();
            _naamvoorspelbaar = rnd.Next(0, 100) <= 50 ? "Vincent" : "John";
            _testresponsedto = testresponsedto;
        }

        public string Naam => _naam;
        public string NaamVoorspelbaar => _naamvoorspelbaar;
        public TestResponseDTO CirculaireRelatieDTO => _testresponsedto;
    }

    public class TestResponseDTO {
        private Random rnd = new();
        private string _naam;
        private string _naamvoorspelbaar;
        private NestedResponseDTO _nestedrespdto;

        public TestResponseDTO() {
            _naam = "Naam" + rnd.Next(int.MinValue, int.MaxValue).ToString();
            _naamvoorspelbaar = rnd.Next(0, 100) <= 50 ? "Henk" : "Jos";
            _nestedrespdto = new NestedResponseDTO(this);
        }

        public string Naam => _naam;
        public string NaamVoorspelbaar => _naamvoorspelbaar;
        public NestedResponseDTO GenesteDTO => _nestedrespdto;
    }

    public interface ICommuniceer {
        List<TestResponseDTO> GeefTestDTOs();
        List<NestedResponseDTO> GeefNestedDTOs();
    }

    public class Communiceerder : ICommuniceer {

        public List<TestResponseDTO> GeefTestDTOs() {
            List<TestResponseDTO> outward = new();
            foreach (int i in Enumerable.Range(100, 200)) {
                outward.Add(new TestResponseDTO());
            }

            return outward;
        }

        public List<NestedResponseDTO> GeefNestedDTOs() {
            List<NestedResponseDTO> outward = new();
            foreach (int i in Enumerable.Range(100, 200)) {
                outward.Add(new NestedResponseDTO());
            }

            return outward;
        }
    }

    public class Zoekmachine {
        private static string _diepteSeparator;

        public Zoekmachine(string diepteSeparator = " >> ") {
            _diepteSeparator = diepteSeparator.StartsWith(" ") && diepteSeparator.EndsWith(" ") ? diepteSeparator : throw new ArgumentException("DiepteSeparator dient minstens 1 spatie aan voor en achterkant te bevatten");
        }

        private KeyValuePair<Type, string> _parseZoekfilter(Type gekozenType, string zoekfilter) {
            if (zoekfilter.Contains(_diepteSeparator)) {
                if (zoekfilter.EndsWith(_diepteSeparator) || zoekfilter.StartsWith(_diepteSeparator)) { throw new ArgumentException("Er ontbreekt een veld."); }

                string[] zoekfilterArgs = zoekfilter.Split(_diepteSeparator);

                Type vorigeType = gekozenType;
                Type huidigType = gekozenType;
                foreach (string naam in zoekfilterArgs) {
                    Type nieuwType = null;
                    foreach (var prop in huidigType.GetProperties()) {
                        if (prop.Name == naam) {
                            vorigeType = huidigType;
                            nieuwType = prop.PropertyType;
                            break;
                        }
                    }
                    if (nieuwType is null) { throw new ArgumentException("Er kon geen type bepaald worden met property naam: " + naam); }
                    huidigType = nieuwType;
                }

                return new KeyValuePair<Type, string>(vorigeType, zoekfilterArgs[zoekfilterArgs.Length - 1]);
            } else {
                return new KeyValuePair<Type, string>(gekozenType, zoekfilter.Trim());
            }
        }

        private object _geefWaardeVanPropertyRecursief(Type targetType, string propertyNaam, object instantie, int maxNiveau=2, int huidigNiveau=1) {
            if(huidigNiveau < 1 || maxNiveau < 1) { throw new ArgumentException("Huidig niveau en max niveau zijn minimum 1. (1,1 = geen recursie)"); }

            var instantieType = instantie.GetType();

            foreach (var property in instantieType.GetProperties()) {

                if ((targetType == property.PropertyType || targetType == instantie.GetType()) 
                    && targetType is not null) {
                    var waarde = property.GetValue(instantie, null);

                    if (property.PropertyType.FullName != "System.String"
                        && !property.PropertyType.IsPrimitive
                        && huidigNiveau < maxNiveau) {

                        int nieuwNiveau = huidigNiveau + 1;
                        var recursieveOperatie = _geefWaardeVanPropertyRecursief(targetType, propertyNaam, waarde, maxNiveau, nieuwNiveau);
                        if (recursieveOperatie is not null) { return recursieveOperatie; }

                    } else if (property.Name == propertyNaam) {
                        return waarde;
                    }
                }
            }

            return null;
        }

        public List<T> ZoekMetFilter<T>(List<Func<List<T>>> dataCollectieActies, string zoekfilter, object zoekterm) {
            
            List<T> dataCollectieResultaat = new();
            List<T> filterDataResultaat = new();

            foreach (Func<List<T>> dataCollectieActie in dataCollectieActies) {
                dataCollectieActie.Invoke()?.ForEach(x => dataCollectieResultaat.Add(x));
            }

            KeyValuePair<Type, string> zoekfilterParseResultaat = _parseZoekfilter(typeof(T), zoekfilter);

            foreach (T b in dataCollectieResultaat) {
                var res = _geefWaardeVanPropertyRecursief(zoekfilterParseResultaat.Key, zoekfilterParseResultaat.Value, b);
                if (res is not null) {
                    if (JsonConvert.SerializeObject((object)res) == JsonConvert.SerializeObject((object)zoekterm)) {
                        filterDataResultaat.Add(b);
                    }
                }

            }

            return filterDataResultaat;
        }

        public List<string> GeefZoekfilterVelden(Type huidigType, List<string> blacklistVelden=null, int maxNiveau=1, int huidigNiveau=1) {
            if (huidigNiveau < 1 || maxNiveau < 1){
                throw new ArgumentException("Huidig niveau en max niveau zijn minimum 1."); }

            if(blacklistVelden is null) { blacklistVelden = new(); }

            List<string> padenOmgevormd = new();
            List<List<string>> paden = new();

            foreach (PropertyInfo p in huidigType.GetProperties()) {
                if (huidigNiveau > maxNiveau) { break; }
                List<string> nieuwPad = new();
                nieuwPad.Add(p.Name);

                if (p.PropertyType.FullName != "System.String"
                    && p.PropertyType.Assembly == Assembly.GetExecutingAssembly()
                    && !p.PropertyType.IsPrimitive
                    && !p.PropertyType.FullName.StartsWith("System.")) {

                    foreach (PropertyInfo t in p.PropertyType.GetProperties()) {
                        nieuwPad.Add(t.Name); paden.Add(nieuwPad.ToList()); nieuwPad.Clear(); nieuwPad.Add(p.Name);
                        List<string> deelPad = GeefZoekfilterVelden(t.PropertyType, null, maxNiveau, huidigNiveau + 1);
                        foreach (string s in deelPad) {
                            nieuwPad.Add(t.Name); nieuwPad.Add(s); paden.Add(nieuwPad.ToList()); nieuwPad.Clear(); nieuwPad.Add(p.Name);
                        }
                    }

                } else {
                    paden.Add(nieuwPad);
                }
            }

            foreach (List<string> ls in paden) {
                bool res = false;

                foreach (string s in ls) {
                    if (res) { continue; } res = blacklistVelden.Any(l => s.Contains(l));
                    if (res) { continue; } res = ls.Any(l => s.Contains(l) && s != l);
                }

                if (!res) {
                    StringBuilder samengesteldPad = new();
                    ls.ForEach(s => samengesteldPad.Append(s + _diepteSeparator));
                    samengesteldPad.Length -= _diepteSeparator.Length;
                    padenOmgevormd.Add(samengesteldPad.ToString());
                }
            }
            return padenOmgevormd;
        }

    }

        class Program {
        static void Main(string[] args) {
            string metricSeparator = "_____________________________________________\n\n";

            Random rnd = new();
            Communiceerder comms = new();
            Zoekmachine zoekmachine = new();

            Console.WriteLine("Zoekfilter velden voor TestResponsDTO");
            List<string> zoekfilterres = zoekmachine.GeefZoekfilterVelden(typeof(TestResponseDTO));
            zoekfilterres.ForEach(x => Console.WriteLine(x));
            Console.WriteLine(metricSeparator);

            Console.WriteLine("Zoekfilter velden voor NestedResponseDTO");
            List<string> zoekfilterres_nest = zoekmachine.GeefZoekfilterVelden(typeof(NestedResponseDTO));
            zoekfilterres_nest.ForEach(x => Console.WriteLine(x));
            Console.WriteLine(metricSeparator);

            List<Func<List<TestResponseDTO>>> dataCollectieActiesTestDTOs = new() { new Func<List<TestResponseDTO>>(comms.GeefTestDTOs) };



			Console.WriteLine("Zonder geneste property --- 200 kop of munt operaties waarvan x met naam 'Henk':");
			List<TestResponseDTO> res = zoekmachine.ZoekMetFilter<TestResponseDTO>(dataCollectieActiesTestDTOs, "NaamVoorspelbaar", "Henk");
			Console.WriteLine("x = " + res.Count);
			string steekproefResultaat = JsonConvert.SerializeObject(
                                            res[rnd.Next(0, res.Count)], 
                                            Formatting.Indented, 
                                            new JsonSerializerSettings() { 
                                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
			Console.WriteLine("Steekproef resultaat:\n" + steekproefResultaat);
			Console.WriteLine(metricSeparator);



			Console.WriteLine("Als geneste property (niveau 1) --- 200 kop of munt operaties waarvan x met naam 'John':");
            List<TestResponseDTO> res_nested = zoekmachine.ZoekMetFilter<TestResponseDTO>(dataCollectieActiesTestDTOs, "GenesteDTO >> NaamVoorspelbaar", "John");
            Console.WriteLine("x = " + res_nested.Count);
			string steekproefResultaat_nested = JsonConvert.SerializeObject(
                                            res_nested[rnd.Next(0, res_nested.Count)],
                                            Formatting.Indented,
                                            new JsonSerializerSettings() { 
                                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
            Console.WriteLine("Steekproef resultaat:\n" + steekproefResultaat_nested);
			Console.WriteLine(metricSeparator);
		}
    }
}

