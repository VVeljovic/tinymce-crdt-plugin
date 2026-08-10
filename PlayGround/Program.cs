using CrdtCore;

var node1 = new CrdtDocument();

node1.LocalInsert('A', 0);
node1.LocalInsert('B', 1);
node1.LocalInsert('C', 2);
node1.LocalInsert('D', 3);
node1.LocalInsert('E', 4);
node1.LocalInsert('F', 5);
node1.LocalInsert('G', 6);


var prede = node1.FindPredecessorId(3);

Console.WriteLine(prede.Counter);
