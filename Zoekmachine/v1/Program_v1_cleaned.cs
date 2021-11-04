using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Newtonsoft.Json;

namespace Zoekmachine.v1 {
    public interface IResponseDTO {
        string Naam { get; }
    }

    public class NestedResponseDTO : IResponseDTO {
        private Random rnd = new();
        private string _naam;
        private string _naamvoorspelbaar;
        public NestedResponseDTO() {
            _naam = "NestedNaam" + rnd.Next(int.MinValue, int.MaxValue).ToString();
            _naamvoorspelbaar = rnd.Next(0, 100) <= 50 ? "Vincent" : "John";
        }
        public string Naam => _naam;
        public string NaamVoorspelbaar => _naamvoorspelbaar;
    }

    public class TestResponseDTO : IResponseDTO {
        private Random rnd = new();
        private string _naam;
        private string _naamvoorspelbaar;
        private NestedResponseDTO _nestedrespdto;
        public TestResponseDTO() {
            _naam = "Naam" + rnd.Next(int.MinValue, int.MaxValue).ToString();
            _naamvoorspelbaar = rnd.Next(0, 100) <= 50 ? "Henk" : "Jos";
            _nestedrespdto = new NestedResponseDTO();
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

    public class RelayCommand : ICommand {

        readonly Action<object> _uittevoeren;
        readonly Predicate<object> _kanUitvoeren;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute) {
            if (execute == null)
                throw new ArgumentNullException("Uit te voeren functie kan niet null zijn");

            _uittevoeren = execute;
            _kanUitvoeren = canExecute;
        }


        [DebuggerStepThrough]
        public bool CanExecute(object parameters) {
            return _kanUitvoeren == null ? true : _kanUitvoeren(parameters);
        }

        public event EventHandler CanExecuteChanged {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void Execute(object parameters) {
            _uittevoeren(parameters);
        }

    }

    public class Zoekmachine {
        private static ICommuniceer _communicatieKanaal;

        private static string diepteSeparator = " >> ";

        private static readonly HashSet<Type> _relationeleTypes =
            new HashSet<Type> {
            typeof(TestResponseDTO), typeof(NestedResponseDTO)
            };

        public Zoekmachine(ICommuniceer communicatieKanaal) {
            _communicatieKanaal = communicatieKanaal;
        }

        private protected void _update<T>(ref T veld, T waarde) {
            if (EqualityComparer<T>.Default.Equals(veld, waarde)) return;

            veld = waarde;
        }

        private object _geefWaardeVanPropertyRecursief(Type targetType, string propertyNaam, object instantie, int diepte = 0) {
            int diepteMax = 0;
            var instantieType = instantie.GetType();

            foreach (var property in instantieType.GetProperties()) {

                if ((targetType == property.PropertyType || targetType == instantie.GetType()) && targetType is not null) {
                    var waarde = property.GetValue(instantie, null);

                    if (property.PropertyType.FullName != "System.String"
                        && !property.PropertyType.IsPrimitive
                        && diepte <= diepteMax) {

                        var recursieveOperatie = _geefWaardeVanPropertyRecursief(targetType, propertyNaam, waarde, diepte++);
                        if (recursieveOperatie is not null) { return recursieveOperatie; }

                    } else if (property.Name == propertyNaam) {
                        return waarde;
                    }
                }
            }

            return null;
        }

        public List<string> GeefZoekfilterVelden(Type DTOType) {
            List<string> velden = new();
            List<string> diepeVelden = new();

            if (_relationeleTypes.Contains(DTOType)) {
                PropertyInfo[] properties = DTOType.GetProperties();

                foreach (PropertyInfo p in properties) {

                    if (_relationeleTypes.Contains(p.PropertyType)) {
                        string veldPrefix = p.Name + diepteSeparator;
                        PropertyInfo[] diepeProperties = p.PropertyType.GetProperties();

                        foreach (PropertyInfo dp in diepeProperties) {
                            if (!_relationeleTypes.Contains(dp.PropertyType)) {
                                diepeVelden.Add(veldPrefix + dp.Name);
                            }
                        }
                    } else {
                        velden.Add(p.Name);
                    }
                }
            } else { throw new ArgumentException("Er kunnen geen zoekfilter velden voor dit type opgevraagd worden."); }

            velden.AddRange(diepeVelden);
            return velden;
        }

        public List<IResponseDTO> ZoekMetFilter(Type DTOType, string zoekfilter, object zoekterm) {
            IList data = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(DTOType));

            Dictionary<Type, ICommand> dataCommandos = new() {
                { typeof(TestResponseDTO), new RelayCommand(p => _update(ref data, _communicatieKanaal.GeefTestDTOs()), p => p is TestResponseDTO) },
                { typeof(NestedResponseDTO), new RelayCommand(p => _update(ref data, _communicatieKanaal.GeefNestedDTOs()), p => p is NestedResponseDTO) }
            };

            if (!dataCommandos.Keys.Contains(DTOType)) { throw new ArgumentException("Dit type kan niet gebruikt worden bij het zoeken."); }


            List<IResponseDTO> resultaat = new();
            string verwerktePropertyNaam = "_invalid";
            Type targetType = null;

            if (zoekfilter.Contains(diepteSeparator)) {
                string[] zoekfilterArgs = zoekfilter.Split(diepteSeparator);
                string klassenaam = zoekfilterArgs[0];

                targetType = Type.GetType(klassenaam) ?? throw new ArgumentException("Kan geen klassenaam bepalen.");
                verwerktePropertyNaam = zoekfilterArgs[1];
            } else {
                targetType = DTOType;
                verwerktePropertyNaam = zoekfilter;
            }

            dataCommandos[DTOType].Execute(DTOType);

            foreach (IResponseDTO b in data) {
                var res = _geefWaardeVanPropertyRecursief(targetType, verwerktePropertyNaam, b);
                if (res is not null) {
                    if (JsonConvert.SerializeObject((object)res) == JsonConvert.SerializeObject((object)zoekterm)) {
                        resultaat.Add(b);
                    }
                }

            }

            return resultaat;
        }



    }
    class Program {
        static void Main1(string[] args) {
            string metricSeparator = "_____________________________________________\n\n";

            Random rnd = new();
            Communiceerder comms = new();
            Zoekmachine zoekmachine = new(comms);

            Console.WriteLine("Zoekfilter velden voor TestResponsDTO");
            List<string> zoekfilterres = zoekmachine.GeefZoekfilterVelden(typeof(TestResponseDTO));
            zoekfilterres.ForEach(x => Console.WriteLine(x));
            Console.WriteLine(metricSeparator);

            Console.WriteLine("Zoekfilter velden voor NestedResponseDTO");
            List<string> zoekfilterres_nest = zoekmachine.GeefZoekfilterVelden(typeof(NestedResponseDTO));
            zoekfilterres_nest.ForEach(x => Console.WriteLine(x));
            Console.WriteLine(metricSeparator);

            Console.WriteLine("Zonder geneste property --- 200 kop of munt operaties waarvan x met naam 'Henk':");
            List<IResponseDTO> res = zoekmachine.ZoekMetFilter(typeof(TestResponseDTO), "NaamVoorspelbaar", "Henk");
            Console.WriteLine("x = " + res.Count);
            string steekproefResultaat = JsonConvert.SerializeObject(res[rnd.Next(0, res.Count)], Formatting.Indented);
            Console.WriteLine("Steekproef resultaat:\n" + steekproefResultaat);
            Console.WriteLine(metricSeparator);

            Console.WriteLine("Zonder geneste property --- 200 kop of munt operaties waarvan x met naam 'Vincent':");
            List<IResponseDTO> res_nested = zoekmachine.ZoekMetFilter(typeof(NestedResponseDTO), "NaamVoorspelbaar", "Vincent");
            Console.WriteLine("x = " + res_nested.Count);
            string steekproefResultaat_nested = JsonConvert.SerializeObject(res_nested[rnd.Next(0, res_nested.Count)], Formatting.Indented);
            Console.WriteLine("Steekproef resultaat:\n" + steekproefResultaat_nested);
            Console.WriteLine(metricSeparator);
        }
    }
}

