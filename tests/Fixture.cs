using System.Text.Json;
if(args.Length<1)return 2;
if(args[0]=="sleep"){Thread.Sleep(10000);return 0;}
if(args[0]=="flood"){for(int i=0;i<100000;i++)Console.WriteLine(new string('x',100));return 0;}
if(args[0]=="fail"){Console.Error.WriteLine("fixture failure");return 7;}
if(args[0]=="echo"){Console.Write(JsonSerializer.Serialize(args.Skip(1)));return 0;}
if(args[0]=="env"){Console.Write(JsonSerializer.Serialize(new{secret=Environment.GetEnvironmentVariable("RESEARCH_SECRET"),factor=Environment.GetEnvironmentVariable("REA_FACTOR"),path=Environment.GetEnvironmentVariable("PATH"),profileInside=Environment.GetEnvironmentVariable("USERPROFILE")!.StartsWith(Environment.CurrentDirectory,StringComparison.OrdinalIgnoreCase)}));return 0;}
if(args[0]=="mutate"){File.AppendAllText(args[1]," ");return 0;}
if(args[0]=="compute"&&args.Length==3){var input=JsonSerializer.Deserialize<Input>(File.ReadAllText(args[1]))!;var factor=int.Parse(Environment.GetEnvironmentVariable("REA_FACTOR")??"2",System.Globalization.CultureInfo.InvariantCulture);if(factor<1||factor>10)return 3;var result=new{factor,values=input.Values.Select(x=>x*factor).ToArray()};var json=JsonSerializer.Serialize(result);File.WriteAllText(args[2],json);Console.WriteLine(json);return 0;}
return 2;
sealed record Input(int[] Values);
