using CrdtCore;


var node = new CrdtDocument();

node.LocalInsert('a', 0);
node.LocalInsert('b', 1);
node.LocalInsert('c', 2);

Console.WriteLine(node.GetText());
Console.ReadLine();
